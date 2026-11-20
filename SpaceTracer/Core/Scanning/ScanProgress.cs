namespace WinTracer.Core.Scanning;

public sealed class ScanProgress
{
    internal long files;
    internal long directories;
    internal long bytes;
    internal long errors;
    private volatile string? _currentPath;

    public long Files => Interlocked.Read(ref files);
    public long Directories => Interlocked.Read(ref directories);
    public long Bytes => Interlocked.Read(ref bytes);
    public long Errors => Interlocked.Read(ref errors);

    public string? CurrentPath
    {
        get => _currentPath;
        internal set => _currentPath = value;
    }
}
