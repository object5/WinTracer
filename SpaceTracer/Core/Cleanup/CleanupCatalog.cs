using WinTracer.Localization;

namespace WinTracer.Core.Cleanup;

public static class CleanupCatalog
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private static readonly string[] ChromiumCacheDirs =
    [
        "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
        "GrShaderCache", "GraphiteDawnCache", "ShaderCache", "GPUPersistentCache", @"Crashpad\reports",
    ];

    private static IEnumerable<PathRule> Chromium(string folder) =>
        ChromiumCacheDirs.Select(d => new PathRule($@"{folder}\{d}"));

    private static IEnumerable<PathRule> Electron(string userData, params string[] extraDirs) =>
        Chromium(userData)
            .Concat(extraDirs.Select(d => new PathRule($@"{userData}\{d}")))
            .Append(new PathRule($@"{userData}\logs") { MinAge = 7 * Day });

    private static PathRule[] Browser(string userDataRoot) =>
    [
        .. Chromium($@"{userDataRoot}\Default"),
        .. Chromium($@"{userDataRoot}\Profile *"),
        .. Chromium(userDataRoot),
    ];

    public static IReadOnlyList<CleanupTarget> Create() =>
    [
        new PathCleanupTarget
        {
            Id = "sys.user-temp", Group = CleanupGroup.System,
            Rules = [new(@"%TEMP%") { MinAge = Day }],
        },
        new PathCleanupTarget
        {
            Id = "sys.windows-temp", Group = CleanupGroup.System, RequiresAdmin = true,
            Rules = [new(@"%WINDIR%\Temp") { MinAge = Day }],
        },
        new PathCleanupTarget
        {
            Id = "sys.wu-download", Group = CleanupGroup.System, RequiresAdmin = true,
            Rules = [new(@"%WINDIR%\SoftwareDistribution\Download") { MinAge = 3 * Day }],
        },
        new PathCleanupTarget
        {
            Id = "sys.delivery-opt", Group = CleanupGroup.System, RequiresAdmin = true,
            Rules = [new(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")],
        },
        new PathCleanupTarget
        {
            Id = "sys.user-dumps", Group = CleanupGroup.System,
            Rules =
            [
                new(@"%LOCALAPPDATA%\CrashDumps"),
                new(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive"),
                new(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue"),
                new(@"%LOCALAPPDATA%\Microsoft\Windows\WER\Temp"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "sys.system-dumps", Group = CleanupGroup.System, RequiresAdmin = true,
            Rules =
            [
                new(@"%WINDIR%\MEMORY.DMP"),
                new(@"%WINDIR%\Minidump"),
                new(@"%WINDIR%\LiveKernelReports") { Pattern = "*.dmp" },
                new(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive"),
                new(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue"),
                new(@"%PROGRAMDATA%\Microsoft\Windows\WER\Temp"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "sys.thumbcache", Group = CleanupGroup.System,
            Rules = [new(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer") { Pattern = "thumbcache_*.db", Recursive = false }],
        },
        new PathCleanupTarget
        {
            Id = "sys.logs", Group = CleanupGroup.System, RequiresAdmin = true,
            Rules =
            [
                new(@"%WINDIR%\Logs\CBS") { MinAge = 7 * Day },
                new(@"%WINDIR%\Logs\DISM") { MinAge = 7 * Day },
                new(@"%WINDIR%\Logs\MoSetup") { MinAge = 7 * Day },
                new(@"%WINDIR%\Logs\WindowsUpdate") { MinAge = 7 * Day },
            ],
        },
        new PathCleanupTarget
        {
            Id = "sys.shader-cache", Group = CleanupGroup.System, SelectedByDefault = false,
            Rules =
            [
                new(@"%LOCALAPPDATA%\D3DSCache"),
                new(@"%LOCALAPPDATA%\NVIDIA\DXCache"),
                new(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
                new(@"%LOCALAPPDATA%\AMD\DxCache"),
                new(@"%LOCALAPPDATA%\AMD\DxcCache"),
                new(@"%LOCALAPPDATA%\AMD\GLCache"),
            ],
        },
        new RecycleBinCleanupTarget
        {
            Id = "sys.recycle-bin", Group = CleanupGroup.System, SelectedByDefault = false,
        },
        new DismComponentCleanupTarget
        {
            Id = "sys.winsxs", Group = CleanupGroup.System, RequiresAdmin = true, SelectedByDefault = false,
        },

        new PathCleanupTarget
        {
            Id = "br.chrome", Group = CleanupGroup.Browsers, ProcessNames = ["chrome"],
            Rules = Browser(@"%LOCALAPPDATA%\Google\Chrome\User Data"),
        },
        new PathCleanupTarget
        {
            Id = "br.edge", Group = CleanupGroup.Browsers, ProcessNames = ["msedge"],
            Rules = Browser(@"%LOCALAPPDATA%\Microsoft\Edge\User Data"),
        },
        new PathCleanupTarget
        {
            Id = "br.yandex", Group = CleanupGroup.Browsers, ProcessNames = ["browser"],
            Rules = Browser(@"%LOCALAPPDATA%\Yandex\YandexBrowser\User Data"),
        },
        new PathCleanupTarget
        {
            Id = "br.brave", Group = CleanupGroup.Browsers, ProcessNames = ["brave"],
            Rules = Browser(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"),
        },
        new PathCleanupTarget
        {
            Id = "br.opera", Group = CleanupGroup.Browsers, ProcessNames = ["opera"],
            Rules =
            [
                .. Chromium(@"%APPDATA%\Opera Software\Opera Stable"),
                .. Chromium(@"%LOCALAPPDATA%\Opera Software\Opera Stable"),
                .. Chromium(@"%APPDATA%\Opera Software\Opera GX Stable"),
                .. Chromium(@"%LOCALAPPDATA%\Opera Software\Opera GX Stable"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "br.firefox", Group = CleanupGroup.Browsers, ProcessNames = ["firefox"],
            Rules =
            [
                new(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"),
                new(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\startupCache"),
                new(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\thumbnails"),
            ],
        },

        new PathCleanupTarget
        {
            Id = "app.discord", Group = CleanupGroup.Applications, ProcessNames = ["Discord"],
            Rules = [.. Electron(@"%APPDATA%\discord")],
        },
        new PathCleanupTarget
        {
            Id = "app.steam", Group = CleanupGroup.Applications, ProcessNames = ["steam", "steamwebhelper"],
            Rules =
            [
                new(@"%LOCALAPPDATA%\Steam\htmlcache"),
                new(@"%PROGRAMFILES(X86)%\Steam\logs") { MinAge = 7 * Day },
                new(@"%PROGRAMFILES(X86)%\Steam\dumps"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "app.vscode", Group = CleanupGroup.Applications, ProcessNames = ["Code"],
            Rules = [.. Electron(@"%APPDATA%\Code", "CachedData", "CachedExtensionVSIXs")],
        },

        new PathCleanupTarget
        {
            Id = "ai.claude-code.cache", Group = CleanupGroup.AiTools, Product = "Claude Code",
            Rules =
            [
                new(@"~\.claude\telemetry"),
                new(@"~\.claude\cache"),
                new(@"~\.claude\debug") { MinAge = Day },
                new(@"~\.claude\shell-snapshots") { MinAge = 2 * Day },
                new(@"~\.claude\paste-cache") { MinAge = 7 * Day },
                new(@"%LOCALAPPDATA%\claude-cli-nodejs\Cache") { MinAge = 3 * Day },
                new(@"%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Local\claude-cli-nodejs\Cache") { MinAge = 3 * Day },
            ],
        },
        new OldVersionsCleanupTarget
        {
            Id = "ai.claude-code.versions", Group = CleanupGroup.AiTools, Product = "Claude Code",
            Folders = [@"~\.local\share\claude\versions"],
            Keep = 2,
        },
        new PathCleanupTarget
        {
            Id = "ai.claude-code.file-history", Group = CleanupGroup.AiTools, Product = "Claude Code",
            SelectedByDefault = false,
            Rules = [new(@"~\.claude\file-history") { MinAge = 30 * Day }],
        },
        new PathCleanupTarget
        {
            Id = "ai.claude-code.sessions", Group = CleanupGroup.AiTools, Product = "Claude Code",
            SelectedByDefault = false,
            Rules = [new(@"~\.claude\projects") { MinAge = 60 * Day, ExcludeDirectories = ["memory"] }],
        },

        new PathCleanupTarget
        {
            Id = "ai.claude-desktop.cache", Group = CleanupGroup.AiTools, Product = "Claude Desktop",
            ProcessNames = ["claude"],
            Rules =
            [
                .. Electron(@"%LOCALAPPDATA%\Claude-Data\*"),
                .. Electron(@"%APPDATA%\Claude"),
                .. Electron(@"%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude"),
                new(@"%LOCALAPPDATA%\Claude\logs") { MinAge = 7 * Day },
                new(@"%LOCALAPPDATA%\Claude-3p\logs") { MinAge = 7 * Day },
            ],
        },
        new OldVersionsCleanupTarget
        {
            Id = "ai.claude-desktop.versions", Group = CleanupGroup.AiTools, Product = "Claude Desktop",
            Folders = [@"%LOCALAPPDATA%\Claude-Data\*\claude-code", @"%LOCALAPPDATA%\Claude-Data\*\claude-code-vm"],
            Keep = 1,
        },

        new PathCleanupTarget
        {
            Id = "ai.codex.cache", Group = CleanupGroup.AiTools, Product = "Codex",
            Rules =
            [
                new(@"~\.codex\cache"),
                new(@"~\.codex\tmp") { MinAge = Day },
                new(@"~\.codex\.tmp") { MinAge = Day },
                new(@"~\.codex\log") { MinAge = 3 * Day },
            ],
        },
        new PathCleanupTarget
        {
            Id = "ai.codex-desktop.cache", Group = CleanupGroup.AiTools, Product = "Codex",
            ProcessNames = ["ChatGPT", "codex"],
            Rules =
            [
                .. Electron(@"%LOCALAPPDATA%\Packages\OpenAI.Codex_*\LocalCache\Roaming\Codex\web\Codex", "component_crx_cache"),
                .. Chromium(@"%LOCALAPPDATA%\Packages\OpenAI.Codex_*\LocalCache\Roaming\Codex\web\Codex\Default"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "ai.codex.sessions", Group = CleanupGroup.AiTools, Product = "Codex",
            SelectedByDefault = false,
            Rules =
            [
                new(@"~\.codex\sessions") { Pattern = "*.jsonl", MinAge = 60 * Day },
                new(@"~\.codex\archived_sessions") { Pattern = "*.jsonl", MinAge = 60 * Day },
            ],
        },

        new PathCleanupTarget
        {
            Id = "ai.opencode.cache", Group = CleanupGroup.AiTools, Product = "OpenCode",
            Rules =
            [
                new(@"~\.local\share\opencode\log") { MinAge = Day },
                new(@"~\.local\share\opencode\tool-output") { MinAge = 7 * Day },
                new(@"~\.cache\opencode"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "ai.opencode-desktop.cache", Group = CleanupGroup.AiTools, Product = "OpenCode",
            ProcessNames = ["OpenCode", "opencode"],
            Rules =
            [
                .. Electron(@"%APPDATA%\ai.opencode.desktop", "compile-cache"),
                new(@"%LOCALAPPDATA%\@opencode-aidesktop-updater"),
                new(@"%LOCALAPPDATA%\@opencodedesktop-updater"),
            ],
        },

        new PathCleanupTarget
        {
            Id = "ai.antigravity.cache", Group = CleanupGroup.AiTools, Product = "Antigravity",
            ProcessNames = ["Antigravity"],
            Rules =
            [
                .. Electron(@"%APPDATA%\Antigravity", "CachedData", "CachedExtensionVSIXs", "CachedProfilesData"),
                new(@"~\.gemini\antigravity\crashes"),
                new(@"~\.gemini\antigravity\cache"),
            ],
        },
        new PathCleanupTarget
        {
            Id = "ai.antigravity.updates", Group = CleanupGroup.AiTools, Product = "Antigravity",
            ProcessNames = ["Antigravity"],
            Rules = [new(@"%LOCALAPPDATA%\antigravity-updater")],
        },

        new PathCleanupTarget
        {
            Id = "dev.nuget", Group = CleanupGroup.Development, SelectedByDefault = false,
            Rules = [new(@"%LOCALAPPDATA%\NuGet\v3-cache"), new(@"%LOCALAPPDATA%\NuGet\plugins-cache")],
        },
        new PathCleanupTarget
        {
            Id = "dev.npm", Group = CleanupGroup.Development, SelectedByDefault = false,
            Rules = [new(@"%LOCALAPPDATA%\npm-cache\_cacache"), new(@"%LOCALAPPDATA%\Yarn\Cache")],
        },
        new PathCleanupTarget
        {
            Id = "dev.python", Group = CleanupGroup.Development, SelectedByDefault = false,
            Rules = [new(@"%LOCALAPPDATA%\pip\cache"), new(@"%LOCALAPPDATA%\uv\cache")],
        },
    ];

    public static string GroupTitle(CleanupGroup group) => L.T($"CleanupGroup.{group}");
}
