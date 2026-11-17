using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GestionnaireJeux.Scanning;

internal static class LauncherHelpers
{
    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string LocalLow => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

    private static readonly string[] HelperExeHints =
    {
        "unitycrashhandler", "crashreport", "crashpad", "crashsight", "unins", "uninstall", "setup", "redist", "vc_",
        "dxsetup", "ace-setup", "easyanticheat", "battleye", "updater", "patcher", "launcher", "helper",
    };

    /// <summary>Lit un fichier même s'il est ouvert par un autre programme (journaux, bases). Garde la fin si trop gros.</summary>
    public static string? ReadShared(string path, long maxBytes = 32L * 1024 * 1024)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > maxBytes) fs.Seek(-maxBytes, SeekOrigin.End);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    public static JsonDocument? ParseJsonFile(string path)
    {
        var text = ReadShared(path);
        if (text == null) return null;
        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch { return null; }
    }

    public static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool IsTrue(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static long? Long(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var l) && l > 0 ? l : null;

    /// <summary>Exécutable principal : un exe Unity (X.exe + dossier X_Data) sinon le plus gros exe non utilitaire.</summary>
    public static string? FindMainExe(string dir, bool requireUnity = false)
    {
        try
        {
            var exes = Directory.GetFiles(dir, "*.exe");
            foreach (var exe in exes)
                if (Directory.Exists(Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + "_Data"))) return exe;
            if (requireUnity) return null;
            return exes.Where(e => !HelperExeHints.Any(h => Path.GetFileName(e).Contains(h, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => new FileInfo(e).Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Associe identifiant → titre en parcourant n'importe quel JSON (objets ayant un id et un « title »).</summary>
    public static void CollectTitles(JsonElement e, Dictionary<string, string> titles)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var title = Str(e, "title");
                if (!string.IsNullOrWhiteSpace(title))
                    foreach (var idName in new[] { "app_name", "appName", "id", "gameId" })
                    {
                        var id = Str(e, idName);
                        if (!string.IsNullOrWhiteSpace(id)) titles.TryAdd(id, title!);
                    }
                foreach (var p in e.EnumerateObject()) CollectTitles(p.Value, titles);
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) CollectTitles(item, titles);
                break;
        }
    }

    /// <summary>« beat-games-beat-saber » → « Beat Games Beat Saber ».</summary>
    public static string Pretty(string slug)
    {
        var words = Regex.Split(slug, @"[-_\s]+").Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }

    public static GameEntry OpenLauncherEntry(string key, string name, GamePlatform platform, string dir, string? launcherExe,
        string launcherName, IReadOnlyList<string> icons, Func<bool> stillInstalled, long? knownSize = null) => new()
    {
        Key = key,
        Name = GameScanner.CleanName(name),
        Platform = platform,
        InstallPath = dir,
        MeasurePath = dir,
        NeedsMeasure = true,
        SizeBytes = knownSize,
        SizeIsApprox = knownSize != null,
        IconSources = icons,
        Kind = launcherExe != null ? UninstallKind.OpenLauncher : UninstallKind.None,
        UninstallCommand = launcherExe != null ? $"\"{launcherExe}\"" : null,
        LauncherName = launcherName,
        StillInstalled = stillInstalled,
    };
}

/// <summary>
/// Jeux Hypergryph via GRYPHLINK (Arknights: Endfield…). Le lanceur chiffre sa configuration :
/// on retrouve les dossiers dans ses journaux (games.log, patch.log) et ceux du jeu Unity (Player.log),
/// puis on ne garde que les dossiers qui contiennent vraiment le jeu.
/// </summary>
internal static class GryphlinkScanner
{
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["endfield"] = "Arknights: Endfield",
        ["arknights"] = "Arknights",
        ["popucom"] = "POPUCOM",
    };

    private static readonly string[] LogNames = { "games.log", "patch.log", "Player.log", "Player-prev.log" };

    private static readonly Regex InstallPathRx = new(@"strGameInstallPath:([^,\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex BaseDirRx = new(@"""base_dir_path""\s*:\s*""((?:[^""\\]|\\.)+)""", RegexOptions.Compiled);
    private static readonly Regex UnityRx = new(@"Discovering subsystems at path (.+?)[\\/][^\\/]+_Data[\\/]UnitySubsystems", RegexOptions.Compiled);

    public static IEnumerable<GameEntry> Scan(ScanContext ctx) => ScanAt(
        Path.Combine(LauncherHelpers.LocalLow, "Gryphline"), FindLauncherDir(ctx), Path.Combine(LauncherHelpers.AppData, "Gryphline"));

    internal static List<GameEntry> ScanAt(string dataRoot, string? launcherDir, string iconsRoot)
    {
        var results = new List<GameEntry>();
        if (launcherDir == null && !Directory.Exists(dataRoot)) return results;

        var launcherExe = launcherDir != null ? Path.Combine(launcherDir, "Launcher.exe") : null;
        if (launcherExe != null && !File.Exists(launcherExe)) launcherExe = null;

        var candidates = new List<string>();
        if (launcherDir != null)
        {
            var gamesDir = Path.Combine(launcherDir, "games");
            if (Directory.Exists(gamesDir)) candidates.AddRange(Directory.GetDirectories(gamesDir));
        }
        if (Directory.Exists(dataRoot))
        {
            foreach (var log in FindLogs(dataRoot))
            {
                var text = LauncherHelpers.ReadShared(log);
                if (text == null) continue;
                foreach (Match m in InstallPathRx.Matches(text)) candidates.Add(m.Groups[1].Value);
                foreach (Match m in UnityRx.Matches(text)) candidates.Add(m.Groups[1].Value);
                foreach (Match m in BaseDirRx.Matches(text))
                {
                    try { candidates.Add(JsonSerializer.Deserialize<string>("\"" + m.Groups[1].Value + "\"") ?? ""); }
                    catch { }
                }
            }
        }

        var iconDirs = Directory.Exists(iconsRoot)
            ? Directory.GetDirectories(iconsRoot).Select(d => Path.Combine(d, "icon")).ToList()
            : new List<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in candidates)
        {
            var dir = Paths.NormalizeDir(raw);
            if (dir == null || !seen.Add(dir)) continue;
            if (dir.Contains(@"\launcher_tmp", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir) || Paths.IsProtected(dir, out _))
                continue;
            var exe = LauncherHelpers.FindMainExe(dir, requireUnity: true);
            if (exe == null) continue;

            var stem = Path.GetFileNameWithoutExtension(exe);
            var name = KnownNames.TryGetValue(stem, out var known) ? known : Path.GetFileName(dir);
            var icons = iconDirs.Select(d => Path.Combine(d, stem.ToLowerInvariant() + ".ico")).Append(exe).ToList();
            var exePath = exe;
            results.Add(LauncherHelpers.OpenLauncherEntry("gryphlink:" + dir.ToLowerInvariant(), name, GamePlatform.Gryphlink, dir,
                launcherExe, "GRYPHLINK", icons, () => File.Exists(exePath)));
        }
        return results;
    }

    private static string? FindLauncherDir(ScanContext ctx)
    {
        using (var launchers = Registry.CurrentUser.OpenSubKey(@"Software\GRYPHLINK\Launcher"))
        {
            if (launchers != null)
                foreach (var id in launchers.GetSubKeyNames())
                {
                    using var k = launchers.OpenSubKey(id);
                    var p = Paths.NormalizeDir(Reg.Str(k, "install_path"));
                    if (p != null && File.Exists(Path.Combine(p, "Launcher.exe"))) return p;
                }
        }
        var entry = ctx.Uninstall.FirstOrDefault(e => e.DisplayName.Equals("GRYPHLINK", StringComparison.OrdinalIgnoreCase));
        var fromEntry = Paths.NormalizeDir(entry?.InstallLocation);
        if (fromEntry != null && Directory.Exists(fromEntry)) return fromEntry;
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GRYPHLINK");
        return Directory.Exists(def) ? def : null;
    }

    private static IEnumerable<string> FindLogs(string root)
    {
        // Journaux du lanceur (<id>\logs\) et du jeu Unity (<Jeu>\Player.log), sans descendre dans les caches Chromium.
        foreach (var sub in GameScannerSafeDirs(root))
        {
            foreach (var name in LogNames)
            {
                var direct = Path.Combine(sub, name);
                if (File.Exists(direct)) yield return direct;
                var inLogs = Path.Combine(sub, "logs", name);
                if (File.Exists(inLogs)) yield return inLogs;
            }
        }
    }

    private static IEnumerable<string> GameScannerSafeDirs(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch { return Array.Empty<string>(); }
    }
}

/// <summary>Jeux installés avec l'appli itch.io (reçu .itch\receipt.json.gz dans chaque dossier de jeu).</summary>
internal static class ItchScanner
{
    private static readonly Regex WinPath = new(@"[A-Za-z]:\\(?:[^\x00-\x1F""<>|*?\\]+\\)*[^\x00-\x1F""<>|*?\\]+", RegexOptions.Compiled);

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var launcher = Path.Combine(LauncherHelpers.LocalAppData, "itch", "itch.exe");
        return ScanAt(Path.Combine(LauncherHelpers.AppData, "itch"), File.Exists(launcher) ? launcher : null);
    }

    internal static List<GameEntry> ScanAt(string appData, string? launcher)
    {
        var results = new List<GameEntry>();
        if (!Directory.Exists(appData)) return results;

        // Emplacement par défaut + emplacements personnalisés cités dans la base de l'appli
        var places = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(appData, "apps") };
        foreach (var f in new[] { Path.Combine(appData, "db", "butler.db"), Path.Combine(appData, "preferences.json") })
        {
            var text = File.Exists(f) ? LauncherHelpers.ReadShared(f, 64L * 1024 * 1024) : null;
            if (text == null) continue;
            foreach (Match m in WinPath.Matches(text.Replace("\\\\", "\\")))
            {
                var p = Paths.NormalizeDir(m.Value);
                if (p != null && Directory.Exists(p) && !Paths.IsProtected(p, out _)) places.Add(p);
            }
        }

        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var place in places)
        {
            if (File.Exists(Receipt(place))) games.Add(place);
            try { foreach (var sub in Directory.GetDirectories(place)) if (File.Exists(Receipt(sub))) games.Add(sub); }
            catch { }
        }

        foreach (var dir in games)
        {
            var receipt = Receipt(dir);
            var title = ReadTitle(receipt) ?? Path.GetFileName(dir);
            var icons = new List<string>();
            var exe = LauncherHelpers.FindMainExe(dir);
            if (exe != null) icons.Add(exe);
            results.Add(LauncherHelpers.OpenLauncherEntry("itch:" + dir.ToLowerInvariant(), title, GamePlatform.Itch, dir,
                launcher, "itch", icons, () => File.Exists(receipt)));
        }
        return results;
    }

    private static string Receipt(string dir) => Path.Combine(dir, ".itch", "receipt.json.gz");

    private static string? ReadTitle(string receipt)
    {
        try
        {
            using var fs = File.OpenRead(receipt);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gz);
            return doc.RootElement.TryGetProperty("game", out var game) ? LauncherHelpers.Str(game, "title") : null;
        }
        catch { return null; }
    }
}

/// <summary>Heroic Games Launcher : jeux Epic (Legendary), GOG (gogdl) et Amazon (Nile) installés par Heroic.</summary>
internal static class HeroicScanner
{
    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var launcher = Path.Combine(LauncherHelpers.LocalAppData, "Programs", "heroic", "Heroic.exe");
        return ScanAt(Path.Combine(LauncherHelpers.AppData, "heroic"), File.Exists(launcher) ? launcher : null);
    }

    internal static List<GameEntry> ScanAt(string cfg, string? launcher)
    {
        var results = new List<GameEntry>();
        if (!Directory.Exists(cfg)) return results;

        // Titres connus (bibliothèques en cache) pour les jeux GOG/Amazon, dont installed.json ne donne pas le nom.
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in new[]
                 {
                     Path.Combine(cfg, "store_cache", "gog_library.json"), Path.Combine(cfg, "gog_store", "library.json"),
                     Path.Combine(cfg, "store_cache", "nile_library.json"), Path.Combine(cfg, "nile_config", "nile", "library.json"),
                 })
        {
            using var doc = File.Exists(lib) ? LauncherHelpers.ParseJsonFile(lib) : null;
            if (doc != null) LauncherHelpers.CollectTitles(doc.RootElement, titles);
        }

        void Add(string store, string id, string? title, string? rawPath, long? size, string? exe)
        {
            var dir = Paths.NormalizeDir(rawPath);
            if (dir == null || !Directory.Exists(dir) || Paths.IsProtected(dir, out _)) return;
            var name = title ?? (titles.TryGetValue(id, out var t) ? t : Path.GetFileName(dir));
            var icons = new List<string>();
            if (!string.IsNullOrWhiteSpace(exe)) icons.Add(Path.IsPathRooted(exe) ? exe : Path.Combine(dir, exe.Replace('/', '\\')));
            var main = LauncherHelpers.FindMainExe(dir);
            if (main != null) icons.Add(main);
            results.Add(LauncherHelpers.OpenLauncherEntry($"heroic:{store}:{id}", name, GamePlatform.Heroic, dir,
                launcher, "Heroic", icons, () => Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(), size));
        }

        // Epic (Legendary) : { "<app>": { "title", "install_path", "install_size", "executable", "is_dlc" } }
        using (var doc = LauncherHelpers.ParseJsonFile(Path.Combine(cfg, "legendaryConfig", "legendary", "installed.json")))
        {
            if (doc?.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    var o = p.Value;
                    if (LauncherHelpers.IsTrue(o, "is_dlc")) continue;
                    Add("epic", p.Name, LauncherHelpers.Str(o, "title"), LauncherHelpers.Str(o, "install_path"),
                        LauncherHelpers.Long(o, "install_size"), LauncherHelpers.Str(o, "executable"));
                }
        }

        // GOG : { "installed": [ { "appName", "install_path", "is_dlc" } ] }
        using (var doc = LauncherHelpers.ParseJsonFile(Path.Combine(cfg, "gog_store", "installed.json")))
        {
            if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("installed", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var o in list.EnumerateArray())
                {
                    if (LauncherHelpers.IsTrue(o, "is_dlc")) continue;
                    var id = LauncherHelpers.Str(o, "appName") ?? LauncherHelpers.Str(o, "app_name") ?? "";
                    Add("gog", id, null, LauncherHelpers.Str(o, "install_path"), null, null);
                }
        }

        // Amazon (Nile) : [ { "id", "path" } ]
        using (var doc = LauncherHelpers.ParseJsonFile(Path.Combine(cfg, "nile_config", "nile", "installed.json")))
        {
            if (doc?.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var o in doc.RootElement.EnumerateArray())
                {
                    var id = LauncherHelpers.Str(o, "id") ?? "";
                    Add("amazon", id, null, LauncherHelpers.Str(o, "path"), LauncherHelpers.Long(o, "size"), null);
                }
        }
        return results;
    }
}

/// <summary>Jeux Meta Quest / Oculus installés sur le PC (bibliothèques Oculus : Manifests\*.json + Software\&lt;nom&gt;).</summary>
internal static class MetaScanner
{
    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? client = null;

        using (var k = Reg.Open(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Oculus VR, LLC\Oculus"))
        {
            var baseDir = Paths.NormalizeDir(Reg.Str(k, "Base"));
            if (baseDir != null)
            {
                roots.Add(Path.Combine(baseDir, "Software"));
                var exe = Path.Combine(baseDir, "Support", "oculus-client", "OculusClient.exe");
                if (File.Exists(exe)) client = exe;
            }
        }
        using (var libs = Registry.CurrentUser.OpenSubKey(@"Software\Oculus VR, LLC\Oculus\Libraries"))
        {
            if (libs != null)
                foreach (var id in libs.GetSubKeyNames())
                {
                    using var lib = libs.OpenSubKey(id);
                    var p = Paths.NormalizeDir(Reg.Str(lib, "OriginalPath"));
                    if (p != null) roots.Add(p);
                }
        }
        return ScanAt(roots, client);
    }

    internal static List<GameEntry> ScanAt(IEnumerable<string> roots, string? client)
    {
        var results = new List<GameEntry>();
        foreach (var root in roots)
        {
            var manifests = Path.Combine(root, "Manifests");
            if (!Directory.Exists(manifests)) continue;
            foreach (var file in GameScanner.SafeFiles(manifests, "*.json"))
            {
                if (file.EndsWith("_assets.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var doc = LauncherHelpers.ParseJsonFile(file);
                    if (doc == null) continue;
                    var o = doc.RootElement;
                    var canonical = LauncherHelpers.Str(o, "canonicalName");
                    if (string.IsNullOrWhiteSpace(canonical) || LauncherHelpers.IsTrue(o, "isCore")
                        || canonical.StartsWith("oculus-", StringComparison.OrdinalIgnoreCase)) continue;
                    var dir = Path.Combine(root, "Software", canonical);
                    if (!Directory.Exists(dir)) continue;
                    var icons = new List<string>();
                    var launch = LauncherHelpers.Str(o, "launchFile");
                    if (!string.IsNullOrWhiteSpace(launch)) icons.Add(Path.Combine(dir, launch.Replace('/', '\\')));
                    var manifestFile = file;
                    results.Add(LauncherHelpers.OpenLauncherEntry("meta:" + canonical, LauncherHelpers.Pretty(canonical), GamePlatform.Meta,
                        dir, client, "Meta Quest Link", icons, () => File.Exists(manifestFile) && Directory.Exists(dir)));
                }
                catch (Exception ex) { Log.Error("meta " + file, ex); }
            }
        }
        return results;
    }
}
