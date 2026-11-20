using System.Runtime.InteropServices;

namespace WinTracer.Core.Native;

public enum FileOperationOutcome
{
    Completed,
    Cancelled,
    Failed,
}

public static class ShellFileOperations
{
    private const uint FO_MOVE = 0x0001;
    private const uint FO_DELETE = 0x0003;

    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static Task<FileOperationOutcome> DeleteAsync(IReadOnlyList<string> paths, bool toRecycleBin, IntPtr owner)
    {
        ushort flags = FOF_NOCONFIRMATION;
        if (toRecycleBin) flags |= FOF_ALLOWUNDO | FOF_WANTNUKEWARNING;
        return RunAsync(FO_DELETE, paths, null, flags, owner);
    }

    public static Task<FileOperationOutcome> MoveAsync(IReadOnlyList<string> paths, string destinationFolder, IntPtr owner) =>
        RunAsync(FO_MOVE, paths, destinationFolder, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR, owner);

    private static Task<FileOperationOutcome> RunAsync(uint func, IReadOnlyList<string> paths, string? to, ushort flags, IntPtr owner)
    {
        if (paths.Count == 0) return Task.FromResult(FileOperationOutcome.Completed);

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = func,
            pFrom = string.Join('\0', paths) + '\0',
            pTo = to is null ? null : to + '\0',
            fFlags = flags,
        };

        var tcs = new TaskCompletionSource<FileOperationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                int rc = SHFileOperation(ref op);
                tcs.SetResult(op.fAnyOperationsAborted ? FileOperationOutcome.Cancelled
                    : rc == 0 ? FileOperationOutcome.Completed
                    : FileOperationOutcome.Failed);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "WinTracer shell file operation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
