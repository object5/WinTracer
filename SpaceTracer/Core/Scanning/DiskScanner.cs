using System.Collections.Concurrent;
using System.IO.Enumeration;
using WinTracer.Core.Native;

namespace WinTracer.Core.Scanning;

public interface IDiskScanner
{
    Task<FsNode> ScanAsync(string rootPath, ScanProgress progress, CancellationToken cancellationToken);
}

public sealed class DiskScanner : IDiskScanner
{
    private const FileAttributes PhysicalSizeAttributes =
        FileAttributes.Compressed | FileAttributes.SparseFile | FileAttributes.Offline |
        (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024,
    };

    public Task<FsNode> ScanAsync(string rootPath, ScanProgress progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return Task.Factory.StartNew(
            () => Scan(rootPath, progress, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static FsNode Scan(string rootPath, ScanProgress progress, CancellationToken ct)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var rootInfo = new DirectoryInfo(fullRoot);
        if (!rootInfo.Exists)
            throw new DirectoryNotFoundException(fullRoot);

        var root = new FsNode(fullRoot, null, FileAttributes.Directory, rootInfo.LastWriteTimeUtc);
        long clusterSize = NativeMethods.GetClusterSize(fullRoot);

        var work = new ConcurrentStack<FsNode>();
        int pending = 1;
        work.Push(root);

        int workerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
        var workers = new Thread[workerCount];
        Exception? fatal = null;

        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = new Thread(() =>
            {
                var spin = new SpinWait();
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (work.TryPop(out var dir))
                        {
                            spin.Reset();
                            ScanDirectory(dir, clusterSize, progress, work, ref pending);
                            Interlocked.Decrement(ref pending);
                        }
                        else if (Volatile.Read(ref pending) == 0)
                        {
                            return;
                        }
                        else
                        {
                            spin.SpinOnce();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref fatal, ex, null);
                }
            })
            {
                IsBackground = true,
                Name = $"WinTracer scanner {i}",
                Priority = ThreadPriority.BelowNormal,
            };
            workers[i].Start();
        }

        foreach (var t in workers) t.Join();

        if (fatal is not null) throw new AggregateException("Disk scan failed.", fatal);
        ct.ThrowIfCancellationRequested();

        progress.CurrentPath = null;
        Aggregate(root);
        return root;
    }

    private static void ScanDirectory(
        FsNode dir, long clusterSize, ScanProgress progress, ConcurrentStack<FsNode> work, ref int pending)
    {
        string path = dir.FullPath;
        progress.CurrentPath = path;
        var children = new List<FsNode>();

        try
        {
            var entries = new FileSystemEnumerable<FsNode>(
                path,
                (ref FileSystemEntry e) => CreateNode(ref e, dir, clusterSize),
                Options);

            foreach (var child in entries)
            {
                children.Add(child);
                if (child.IsDirectory)
                {
                    Interlocked.Increment(ref progress.directories);
                }
                else
                {
                    Interlocked.Increment(ref progress.files);
                    Interlocked.Add(ref progress.bytes, child.Size);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            dir.AccessDenied = true;
            Interlocked.Increment(ref progress.errors);
        }

        dir.Children = children;

        foreach (var child in children)
        {
            if (child.IsDirectory && !child.IsReparsePoint)
            {
                Interlocked.Increment(ref pending);
                work.Push(child);
            }
        }
    }

    private static FsNode CreateNode(ref FileSystemEntry entry, FsNode parent, long clusterSize)
    {
        var attrs = entry.Attributes;
        var node = new FsNode(entry.FileName.ToString(), parent, attrs, entry.LastWriteTimeUtc.UtcDateTime);

        if (!entry.IsDirectory)
        {
            long length = entry.Length;
            node.Size = length;

            long allocated = -1;
            if ((attrs & PhysicalSizeAttributes) != 0)
            {
                allocated = NativeMethods.TryGetCompressedFileSize(entry.ToFullPath());
                if (allocated >= 0)
                    allocated = RoundUp(allocated, clusterSize);
            }
            node.Allocated = allocated >= 0 ? allocated : RoundUp(length, clusterSize);
        }

        return node;
    }

    private static long RoundUp(long value, long multiple) =>
        value <= 0 ? 0 : (value + multiple - 1) / multiple * multiple;

    private static void Aggregate(FsNode root)
    {
        var order = new List<FsNode>();
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            order.Add(n);
            if (n.Children is null) continue;
            foreach (var c in n.Children)
                if (c.IsDirectory) stack.Push(c);
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var n = order[i];
            if (n.Children is null) { n.Children = []; continue; }

            long size = 0, allocated = 0;
            int files = 0, dirs = 0;
            foreach (var c in n.Children)
            {
                size += c.Size;
                allocated += c.Allocated;
                if (c.IsDirectory)
                {
                    files += c.FileCount;
                    dirs += c.DirCount + 1;
                }
                else
                {
                    files++;
                }
            }

            n.Size = size;
            n.Allocated = allocated;
            n.FileCount = files;
            n.DirCount = dirs;
            n.Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
            n.Children.TrimExcess();
        }
    }
}
