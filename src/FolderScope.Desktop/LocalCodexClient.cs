using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace FolderScope.Desktop;

internal static class LocalCodexClient
{
    internal static string? FindExecutable(string preferred = "codex")
    {
        if (Path.IsPathFullyQualified(preferred))
            return Path.GetExtension(preferred).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(preferred) ? preferred : null;
        if (!preferred.Equals("codex", StringComparison.OrdinalIgnoreCase)
            && !preferred.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)) return null;

        // An Explorer shortcut can inherit PATH from before the CLI was installed.
        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var paths = Environment.GetEnvironmentVariable("PATH", scope) ?? "";
            foreach (var directory in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(directory.Trim('"'));
                if (!Path.IsPathFullyQualified(expanded)) continue;
                var candidate = Path.Combine(expanded, "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    internal static ProcessStartInfo StartInfo(ModelProfile profile, IEnumerable<string> arguments, string directory)
    {
        var executable = FindExecutable(profile.CodexCommand) ?? throw new InvalidOperationException("未找到本机 codex.exe，请填写其完整路径。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory
        };
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            ApplySystemProxy(start, settings?.GetValue("ProxyEnable") is int enabled && enabled != 0,
                settings?.GetValue("ProxyServer") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void ApplySystemProxy(ProcessStartInfo start, bool enabled, string? server)
    {
        // Respect explicit process proxy settings. Only inherit the enabled Windows proxy as fallback.
        if (!enabled || string.IsNullOrWhiteSpace(server) || new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" }
            .Any(name => start.Environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))) return;
        var parts = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var address = parts.FirstOrDefault(part => part.StartsWith("https=", StringComparison.OrdinalIgnoreCase))
            ?? parts.FirstOrDefault(part => part.StartsWith("http=", StringComparison.OrdinalIgnoreCase))
            ?? parts.FirstOrDefault(part => !part.Contains('='));
        if (address is null) return;
        if (address.Contains('=')) address = address[(address.IndexOf('=') + 1)..];
        if (!address.Contains("://")) address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)) return;
        start.Environment["HTTPS_PROXY"] = uri.AbsoluteUri;
        start.Environment["HTTP_PROXY"] = uri.AbsoluteUri;
    }

    internal static string FailureMessage(int exitCode, string error)
    {
        // Do not expose raw CLI output: it can contain prompts, account data or credentials.
        if (error.Contains("invalid utf-8", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not valid utf-8", StringComparison.OrdinalIgnoreCase))
            return "Codex CLI 无法读取分析文字，请更新 Flash - Folder 和本机 Codex CLI 后重试。";
        if (error.Contains("model_not_found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("model is not supported", StringComparison.OrdinalIgnoreCase))
            return "当前登录账户不支持所选 Codex 模型，请在模型设置中更换模型 ID，或留空使用 CLI 内置默认模型。";
        if (error.Contains("10061") || error.Contains("failed to connect", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
            return "Codex 无法连接服务，请检查网络及 Windows 代理是否可用。";
        if (error.Contains("401") || error.Contains("not logged in", StringComparison.OrdinalIgnoreCase)
            || error.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return "Codex 登录已失效，请在本机重新登录后重试。";
        if (error.Contains("429") || error.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || error.Contains("usage limit", StringComparison.OrdinalIgnoreCase))
            return "Codex 当前额度或请求频率受限，请稍后重试或检查账户额度。";
        return $"Codex CLI 未完成回复（退出码 {exitCode}），请在模型设置中测试连接。";
    }

    internal static async Task TestAsync(ModelProfile profile, CancellationToken token)
    {
        var directory = Path.GetTempPath();
        var version = await RunAsync(StartInfo(profile, new[] { "--version" }, directory), null, token);
        if (version.ExitCode != 0 || !version.Text.Contains("codex", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Codex CLI 无法启动，请核对可执行文件路径。");
        var login = await RunAsync(StartInfo(profile, new[] { "login", "status" }, directory), null, token);
        if (login.ExitCode != 0)
            throw new InvalidOperationException("本机 Codex 尚未登录，请在终端完成 codex login 后重试。");
        await AnalyzeAsync(profile, "连接测试，请只回复 OK，不调用工具。",
            new[] { new AnalysisChat.Turn("user", "OK") }, token);
    }

    internal static async Task<string> AnalyzeAsync(ModelProfile profile, string system,
        IReadOnlyList<AnalysisChat.Turn> turns, CancellationToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FolderScope-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "reply.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var arguments = new List<string> { "exec", "--ignore-user-config", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only",
                "--color", "never", "--output-last-message", output };
            if (!string.IsNullOrWhiteSpace(profile.Model)) { arguments.Add("--model"); arguments.Add(profile.Model); }
            arguments.Add("-");
            var prompt = new StringBuilder(system);
            prompt.AppendLine("\n仅根据下列材料回答。不要执行命令、读取文件或调用工具。");
            foreach (var turn in turns)
                prompt.AppendLine().AppendLine(turn.Role == "assistant" ? "先前助手回复" : "用户材料或问题").AppendLine(turn.Content);
            var result = await RunAsync(StartInfo(profile, arguments, directory), prompt.ToString(), timeout.Token);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(FailureMessage(result.ExitCode, result.Error));
            if (!File.Exists(output) || new FileInfo(output).Length > 4 * 1024 * 1024)
                throw new InvalidOperationException("Codex CLI 没有返回可用的回复文件。");
            var answer = await File.ReadAllTextAsync(output, timeout.Token);
            if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("Codex CLI 回复为空。");
            return answer;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException("Codex 回复超时，请检查网络或稍后重试。");
        }
        finally
        {
            try
            {
                if (File.Exists(output)) File.Delete(output);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<(int ExitCode, string Text, string Error)> RunAsync(ProcessStartInfo start, string? input, CancellationToken token)
    {
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Codex CLI 未能启动。");
            var stdout = ReadBounded(process.StandardOutput, token);
            var stderr = ReadBounded(process.StandardError, token);
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            var text = await stdout;
            var error = await stderr;
            return (process.ExitCode, text, error);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("无法启动本机 Codex CLI，请检查可执行文件路径。");
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) return text.ToString();
            if (text.Length + count > 4 * 1024 * 1024) throw new InvalidOperationException("Codex CLI 输出超出大小限制。");
            text.Append(buffer, 0, count);
        }
    }
}
