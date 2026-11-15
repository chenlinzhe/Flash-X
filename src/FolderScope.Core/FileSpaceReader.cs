using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace FolderScope.Core;

public readonly record struct FileIdentity(ulong Volume, ulong Low, ulong High);
public sealed record FileSpaceInfo(long LogicalBytes, long AllocatedBytes, uint LinkCount, FileIdentity? Identity = null);
public static class FileSpaceReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IdInfo { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdInfo(SafeFileHandle handle, int infoClass, out IdInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out StandardInfo info, uint size);
    public static bool MayRecallData(FileAttributes attributes) => ((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0;
    public static FileSpaceInfo Read(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var full = Path.GetFullPath(path);
        var attributes = File.GetAttributes(full);
        if (MayRecallData(attributes)) throw new IOException("Offline or recall-marked file: allocated space left unknown to avoid recalling content.");
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("File is now a directory or reparse point; allocation was not queried.");
        using var handle = CreateFileW(full, 0, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot open file metadata.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if (!GetFileInformationByHandleEx(handle, 1, out var info, (uint)Marshal.SizeOf<StandardInfo>()))
            throw new IOException("Cannot query allocation metadata.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if (info.Directory != 0 || info.AllocationSize < 0 || info.EndOfFile < 0) throw new InvalidDataException("Invalid file allocation metadata.");
        FileIdentity? identity = info.NumberOfLinks > 1 && GetIdInfo(handle, 18, out var id, (uint)Marshal.SizeOf<IdInfo>()) ? new FileIdentity(id.Volume, id.Low, id.High) : null;
        return new(info.EndOfFile, info.AllocationSize, info.NumberOfLinks, identity);
    }
}