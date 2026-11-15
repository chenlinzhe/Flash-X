using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FolderScope.Core;

public static class AllocatedSpace
{
    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSize(string fileName, out uint high);

    public static long ReadFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Allocated space requires Windows.");
        var full = Path.GetFullPath(path);
        var attributes = File.GetAttributes(full);
        if (FileSpaceReader.MayRecallData(attributes)) throw new IOException("Offline or recall-marked file: allocated space left unknown to avoid recalling content.");
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new ArgumentException("Only regular files are supported.", nameof(path));
        Marshal.SetLastPInvokeError(0);
        var low = GetCompressedFileSize(full, out var high);
        var error = Marshal.GetLastPInvokeError();
        if (low == uint.MaxValue && error != 0) throw new IOException("Allocated space query failed.", new Win32Exception(error));
        return checked((long)(((ulong)high << 32) | low));
    }
}
