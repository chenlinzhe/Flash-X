using System.IO;
using System.Diagnostics;
using System.Globalization;
namespace FolderScope.Desktop;

internal static class ResponsivenessWatchdog
{
    private const int MaximumRestartAttempts = 3;
    private static readonly CancellationTokenSource Lifetime = new();
    private static string? _failureReason;
    private static Task? _supervision;
    public static string? FailureReason => Volatile.Read(ref _failureReason);
    // Raised on the supervisor thread; UI subscribers must dispatch to their window.
    public static event Action<string>? ProtectionFailed;

    public static void Start()
    {
        var executable = Environment.ProcessPath;
        if (executable is null || !executable.EndsWith("FolderScope.Desktop.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请通过 FolderScope.Desktop.exe 启动，以启用独立无响应保护。");
        using var current = Process.GetCurrentProcess();
        var processId = current.Id;
        var startTicks = current.StartTime.ToUniversalTime().Ticks;
        var watcher = Launch(executable, processId, startTicks);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Lifetime.Cancel();
        // The --watch-responsiveness entry point bypasses Start, so helpers never supervise helpers.
        _supervision = Task.Run(() => Supervise(watcher, executable, processId, startTicks, Lifetime.Token));
    }

    private static Process Launch(string executable, int processId, long startTicks)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--watch-responsiveness");
        start.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(startTicks.ToString(CultureInfo.InvariantCulture));
        var watcher = Process.Start(start) ?? throw new InvalidOperationException("独立无响应看护进程未启动。");
        try
        {
            watcher.BeginOutputReadLine(); watcher.BeginErrorReadLine();
            if (watcher.WaitForExit(250)) throw new InvalidOperationException("独立无响应看护进程启动后提前退出。");
            return watcher;
        }
        catch
        {
            // This handle is the helper just created here, never a process discovered by name.
            try { if (!watcher.HasExited) watcher.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            watcher.Dispose();
            throw;
        }
    }

    private static async Task Supervise(Process watcher, string executable, int processId, long startTicks, CancellationToken token)
    {
        Process? active = watcher;
        var attempts = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (active is not null)
                {
                    try { await active.WaitForExitAsync(token).ConfigureAwait(false); }
                    finally { active.Dispose(); active = null; }
                    token.ThrowIfCancellationRequested();
                    Record(processId, startTicks, "watcher-exited-unexpectedly");
                }
                if (attempts >= MaximumRestartAttempts)
                {
                    var reason = "无响应保护进程反复退出，3次恢复尝试均已用尽。当前自动退出保护不可用，请保存结果并重新打开软件。";
                    ReportProtectionFailure(processId, startTicks, "restart-limit", reason);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                attempts++;
                Record(processId, startTicks, "watcher-restart-attempt-" + attempts.ToString(CultureInfo.InvariantCulture));
                try
                {
                    active = Launch(executable, processId, startTicks);
                    Record(processId, startTicks, "watcher-restarted-" + attempts.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                {
                    Record(processId, startTicks, "watcher-restart-failed-" + ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            if (!token.IsCancellationRequested)
                ReportProtectionFailure(processId, startTicks, "supervisor-" + ex.GetType().Name,
                    "无响应保护状态无法继续确认，请保存结果并重新打开软件。" + ex.Message);
        }
        finally { active?.Dispose(); }
    }
    private static void ReportProtectionFailure(int processId, long startTicks, string cause, string reason)
    {
        Volatile.Write(ref _failureReason, reason);
        Record(processId, startTicks, "protection-unavailable-" + cause);
        // A closing window must not turn the supervisor notification into an unobserved task failure.
        var subscribers = ProtectionFailed;
        if (subscribers is null) return;
        foreach (Action<string> subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(reason); }
            catch (Exception ex) { Record(processId, startTicks, "notification-failed-" + ex.GetType().Name); }
        }
    }
    public static void Watch(int processId, long startTicks)
    {
        var elapsed = Stopwatch.StartNew();
        var deadline = new FolderScope.Core.UnresponsiveDeadline(TimeSpan.FromMinutes(3));
        try
        {
            using var target = Process.GetProcessById(processId);
            if (target.StartTime.ToUniversalTime().Ticks != startTicks
                || !string.Equals(target.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            Record(processId, startTicks, "watching");
            var wasUnresponsive = false;
            while (!target.WaitForExit(2000))
            {
                target.Refresh();
                var responding = target.MainWindowHandle == IntPtr.Zero || target.Responding;
                if (!responding && !wasUnresponsive) Record(processId, startTicks, "unresponsive-detected");
                if (responding && wasUnresponsive) Record(processId, startTicks, "responsiveness-recovered");
                wasUnresponsive = !responding;
                if (!deadline.Observe(responding, elapsed.Elapsed)) continue;
                // Recheck identity and responsiveness immediately before terminating only this process.
                target.Refresh();
                if (target.StartTime.ToUniversalTime().Ticks == startTicks && !target.Responding)
                {
                    Record(processId, startTicks, "timeout-180s-termination-requested");
                    target.Kill();
                    Record(processId, startTicks, target.WaitForExit(10000) ? "termination-confirmed" : "termination-not-confirmed");
                    return;
                }
                wasUnresponsive = false;
                deadline.Observe(true, elapsed.Elapsed);
                Record(processId, startTicks, "recovered-before-termination");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { Record(processId, startTicks, "watch-failed-" + ex.GetType().Name); }
    }
    private static void Record(int processId, long startTicks, string eventName)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderScope");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"watchdog-{processId}-{startTicks}.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O} pid={processId} {eventName}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }}