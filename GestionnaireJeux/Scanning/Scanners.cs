using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GestionnaireJeux.Services;
using Microsoft.Win32;

namespace GestionnaireJeux.Scanning;

internal static class SteamScanner
{
    // Redistribuables et outils techniques de Steam, inutiles à lister
    private static readonly HashSet<string> ExcludedIds = new() { "228980", "1007" };
    private static readonly string[] ExcludedPrefixes = { "Steamworks", "Steam Linux Runtime", "Proton ", "Proton-" };

    public static string? FindSteamPath()
    {
        string? found = null;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            var p = Paths.NormalizeDir(Reg.Str(k, "SteamPath"));
            if (p != null && Directory.Exists(p)) found = p;
        }
        if (found == null)
        {
            using var k = Reg.Open(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam");
            var p = Paths.NormalizeDir(Reg.Str(k, "InstallPath"));
            if (p != null && Directory.Exists(p)) found = p;
        }
        // SteamPath est souvent en minuscules (« c:/program files (x86)/steam ») : on récupère la vraie casse.
        return found == null ? null : Paths.NormalizeDir(Native.GetFinalPath(found)) ?? found;
    }

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        var steam = FindSteamPath();
        if (steam == null) return results;

        var libraries = new List<string> { steam };
        var foldersFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(foldersFile))
        {
            try
            {
                var folders = Vdf.Parse(File.ReadAllText(foldersFile)).Children.Values.FirstOrDefault();
                if (folders != null)
                {
                    foreach (var child in folders.Children.Values)
                        if (child.Values.TryGetValue("path", out var p)) libraries.Add(p);
                    foreach (var (k, v) in folders.Values) // ancien format : "1" "D:\\SteamLibrary"
                        if (int.TryParse(k, out _)) libraries.Add(v);
                }
            }
            catch (Exception ex) { Log.Error("libraryfolders.vdf", ex); }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in libraries)
        {
            var lib = Paths.NormalizeDir(raw);
            if (lib == null || !seen.Add(lib)) continue;
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in GameScanner.SafeFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var g = ReadManifest(ctx, steam, apps, manifest);
                    if (g != null) results.Add(g);
                }
                catch (Exception ex) { Log.Error(manifest, ex); }
            }
        }
        return results;
    }

    private static GameEntry? ReadManifest(ScanContext ctx, string steam, string apps, string manifest)
    {
        var state = Vdf.Parse(File.ReadAllText(manifest)).Children.Values.FirstOrDefault();
        if (state == null) return null;
        state.Values.TryGetValue("appid", out var appId);
        state.Values.TryGetValue("name", out var name);
        state.Values.TryGetValue("installdir", out var installDir);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(installDir)) return null;
        var reg = ctx.Take("Steam App " + appId);
        // Steam écrit parfois un nom provisoire « appid_250820 » : on prend alors celui du registre.
        if (string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name, @"^appid_\d+$", RegexOptions.IgnoreCase))
            name = reg?.DisplayName ?? installDir;
        name = GameScanner.CleanName(name);
        if (ExcludedIds.Contains(appId) || ExcludedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return null;

        var path = Path.Combine(apps, "common", installDir);
        long? size = state.Values.TryGetValue("SizeOnDisk", out var sz) && long.TryParse(sz, out var s) && s > 0 ? s : null;
        DateTime? lastPlayed = state.Values.TryGetValue("LastPlayed", out var lp) && long.TryParse(lp, out var t) && t > 0
            ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime
            : null;

        var icons = new List<string>();
        if (reg?.DisplayIcon != null) icons.Add(reg.DisplayIcon);
        var cached = CachedIcon(steam, appId);
        if (cached != null) icons.Add(cached);

        bool exists = Directory.Exists(path);
        return new GameEntry
        {
            Key = "steam:" + appId,
            Name = name,
            Platform = GamePlatform.Steam,
            InstallPath = path,
            MeasurePath = path,
            NeedsMeasure = size == null && exists,
            SizeBytes = size,
            LastPlayed = lastPlayed,
            IconSources = icons,
            Kind = UninstallKind.Uri,
            UninstallUri = "steam://uninstall/" + appId,
            StillInstalled = () => File.Exists(manifest),
        };
    }

    private static string? CachedIcon(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var dir = Path.Combine(cache, appId);
        if (Directory.Exists(dir))
        {
            // Nouveau format : <appid>\<sha1>.jpg = icône
            foreach (var f in GameScanner.SafeFiles(dir, "*.jpg"))
                if (Path.GetFileNameWithoutExtension(f).Length == 40) return f;
        }
        var old = Path.Combine(cache, appId + "_icon.jpg");
        return File.Exists(old) ? old : null;
    }
}

internal static class EpicScanner
{
    public static string LauncherInstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");

    private sealed record Item(string File, string AppName, string DisplayName, string InstallLocation,
        string? MainGame, long Size, string? LaunchExe, bool Incomplete);

    private static string? ManifestsDir()
    {
        using (var k = Reg.Open(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Epic Games\EpicGamesLauncher"))
        {
            var data = Paths.NormalizeDir(Reg.Str(k, "AppDataPath"));
            if (data != null)
            {
                var m = Path.Combine(data, "Manifests");
                if (Directory.Exists(m)) return m;
            }
        }
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        return Directory.Exists(def) ? def : null;
    }

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        var dir = ManifestsDir();
        if (dir == null) return results;

        var items = new List<Item>();
        foreach (var file in GameScanner.SafeFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var r = doc.RootElement;
                string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var app = S("AppName");
                var loc = Paths.NormalizeDir(S("InstallLocation"));
                if (string.IsNullOrWhiteSpace(app) || loc == null) continue;
                long size = r.TryGetProperty("InstallSize", out var isz) && isz.ValueKind == JsonValueKind.Number && isz.TryGetInt64(out var v) ? v : 0;
                bool incomplete = r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True;
                items.Add(new Item(file, app, GameScanner.CleanName(S("DisplayName") ?? app), loc, S("MainGameAppName"), size, S("LaunchExecutable"), incomplete));
            }
            catch (Exception ex) { Log.Error(file, ex); }
        }

        static bool IsDlc(Item i) => !string.IsNullOrWhiteSpace(i.MainGame) && !i.MainGame.Equals(i.AppName, StringComparison.OrdinalIgnoreCase);
        var dat = File.Exists(LauncherInstalledPath) ? LauncherInstalledPath : null;

        foreach (var main in items.Where(i => !IsDlc(i)))
        {
            var dlcs = items.Where(i => IsDlc(i) && i.MainGame!.Equals(main.AppName, StringComparison.OrdinalIgnoreCase)).ToList();
            var icons = new List<string>();
            if (!string.IsNullOrWhiteSpace(main.LaunchExe))
                icons.Add(Path.Combine(main.InstallLocation, main.LaunchExe.Replace('/', '\\')));
            long estimate = main.Size + dlcs.Sum(d => d.Size);
            var manifestFile = main.File;
            var location = main.InstallLocation;
            results.Add(new GameEntry
            {
                Key = "epic:" + main.AppName,
                Name = main.DisplayName,
                Platform = GamePlatform.Epic,
                InstallPath = location,
                MeasurePath = location,
                NeedsMeasure = Directory.Exists(location),
                SizeBytes = estimate > 0 ? estimate : null,
                SizeIsApprox = true,
                IconSources = icons,
                Note = main.Incomplete ? "NoteIncomplete" : null,
                Kind = UninstallKind.EpicDirect,
                Epic = new EpicRemovalInfo
                {
                    InstallPath = location,
                    ManifestPaths = new[] { main.File }.Concat(dlcs.Select(d => d.File)).ToList(),
                    AppNames = new[] { main.AppName }.Concat(dlcs.Select(d => d.AppName)).ToList(),
                    LauncherInstalledPath = dat,
                },
                StillInstalled = () => File.Exists(manifestFile) && Directory.Exists(location),
            });
        }
        return results;
    }
}

/// <summary>Jeux Xbox / Game Pass / Microsoft Store (paquets MSIX), y compris ceux installés dans X:\XboxGames.</summary>
internal static class XboxScanner
{
    private const string RepoPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    // Jeux UWP « classiques » qui n'ont pas de MicrosoftGame.config
    private static readonly string[] KnownStoreGames =
    {
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.MicrosoftMahjong", "Microsoft.MicrosoftJigsaw",
        "Microsoft.MicrosoftSudoku", "Microsoft.MicrosoftMinesweeper", "Microsoft.MicrosoftUltimateWordGames",
        "Microsoft.MicrosoftTreasureHunt", "Microsoft.MinecraftUWP", "Microsoft.MinecraftEducationEdition",
        "king.com.", "GAMELOFTSA.", "A278AB0D.",
    };

    private sealed record Candidate(string FullName, string Name, Version Version, string Root, string? DisplayName);

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        using var repo = Registry.CurrentUser.OpenSubKey(RepoPath);
        if (repo == null) return results;

        var best = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in repo.GetSubKeyNames())
        {
            try
            {
                var parts = full.Split('_');
                if (parts.Length < 5) continue;
                var name = parts[0];
                using var k = repo.OpenSubKey(full);
                var root = Paths.NormalizeDir(Reg.Str(k, "PackageRootFolder"));
                if (root == null || !Directory.Exists(root)) continue;

                bool isGame = File.Exists(Path.Combine(root, "MicrosoftGame.config"))
                              || KnownStoreGames.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                if (!isGame) continue;

                // Les DLC / extensions n'ont pas d'<Application> : on les ignore.
                var appx = Path.Combine(root, "AppxManifest.xml");
                if (File.Exists(appx) && !File.ReadAllText(appx).Contains("<Application ", StringComparison.Ordinal)) continue;

                if (!Version.TryParse(parts[1], out var version)) version = new Version(0, 0);
                var family = name + "_" + parts[^1];
                var cand = new Candidate(full, name, version, root, Reg.Str(k, "DisplayName"));
                if (!best.TryGetValue(family, out var current) || current.Version < version) best[family] = cand;
            }
            catch (Exception ex) { Log.Error("xbox " + full, ex); }
        }

        foreach (var c in best.Values)
        {
            try { results.Add(Build(c)); }
            catch (Exception ex) { Log.Error("xbox " + c.FullName, ex); }
        }
        return results;
    }

    private static GameEntry Build(Candidate c)
    {
        // C:\Program Files\WindowsApps\<paquet> est souvent une jonction vers X:\XboxGames\<Jeu>\Content
        var real = ResolveLink(c.Root);
        var gameFolder = real;
        if (Path.GetFileName(real).Equals("Content", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(real);
            if (parent != null && !Paths.IsDriveRoot(parent)) gameFolder = parent;
        }

        XElement? visuals = null;
        var cfg = Path.Combine(real, "MicrosoftGame.config");
        if (File.Exists(cfg))
        {
            try { visuals = XDocument.Load(cfg).Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals"); }
            catch { }
        }

        string? name = visuals?.Attribute("DefaultDisplayName")?.Value;
        if (IsUnresolved(name)) name = null;
        if (name == null && c.DisplayName != null)
            name = c.DisplayName.StartsWith("@{", StringComparison.Ordinal) ? Native.LoadIndirectString(c.DisplayName) : c.DisplayName;
        if (IsUnresolved(name)) name = AppxDisplayName(real, c);
        if (IsUnresolved(name)) name = c.Name;

        var icons = new List<string>();
        var logo = ConfigLogo(real, visuals) ?? AppxLogo(real);
        if (logo != null) icons.Add(logo);

        var fullName = c.FullName;
        var root = c.Root;
        return new GameEntry
        {
            Key = "xbox:" + c.Name,
            Name = GameScanner.CleanName(name!),
            Platform = GamePlatform.Xbox,
            InstallPath = gameFolder,
            MeasurePath = gameFolder,
            NeedsMeasure = true,
            IconSources = icons,
            Kind = UninstallKind.Appx,
            PackageFullName = fullName,
            StillInstalled = () => Reg.Exists(RegistryHive.CurrentUser, RegistryView.Registry64, RepoPath + "\\" + fullName)
                                   && Directory.Exists(root),
        };
    }

    private static bool IsUnresolved(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("@{", StringComparison.Ordinal);

    private static string ResolveLink(string path)
    {
        var final = Paths.NormalizeDir(Native.GetFinalPath(path));
        if (final != null && Directory.Exists(final)) return final;
        try
        {
            var di = new DirectoryInfo(path);
            if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var t = Paths.NormalizeDir(di.ResolveLinkTarget(returnFinalTarget: true)?.FullName);
                if (t != null && Directory.Exists(t)) return t;
            }
        }
        catch (Exception ex) { Log.Error("jonction " + path, ex); }
        return path;
    }

    private static string? ConfigLogo(string folder, XElement? visuals)
    {
        if (visuals == null) return null;
        foreach (var attr in new[] { "Square150x150Logo", "Square480x480Logo", "Square44x44Logo", "StoreLogo" })
        {
            var found = FindAsset(folder, visuals.Attribute(attr)?.Value);
            if (found != null) return found;
        }
        return null;
    }

    private static string? AppxLogo(string folder)
    {
        try
        {
            var appx = Path.Combine(folder, "AppxManifest.xml");
            if (!File.Exists(appx)) return null;
            var logo = XDocument.Load(appx).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Logo" && e.Parent?.Name.LocalName == "Properties")?.Value;
            return FindAsset(folder, logo);
        }
        catch { return null; }
    }

    /// <summary>Trouve "Logo.png" ou ses variantes "Logo.scale-200.png", "Logo.targetsize-48.png"…</summary>
    private static string? FindAsset(string folder, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        try
        {
            var p = Path.Combine(folder, relative.Replace('/', '\\'));
            if (File.Exists(p)) return p;
            var dir = Path.GetDirectoryName(p);
            if (dir == null || !Directory.Exists(dir)) return null;
            var stem = Path.GetFileNameWithoutExtension(p);
            var ext = Path.GetExtension(p);
            return Directory.EnumerateFiles(dir, stem + ".*" + ext)
                .Where(f => !f.Contains("contrast-", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? AppxDisplayName(string folder, Candidate c)
    {
        try
        {
            var appx = Path.Combine(folder, "AppxManifest.xml");
            if (!File.Exists(appx)) return null;
            var dn = XDocument.Load(appx).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "DisplayName" && e.Parent?.Name.LocalName == "Properties")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(dn)) return null;
            if (!dn.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return dn;
            var res = dn["ms-resource:".Length..];
            var uri = res.StartsWith("//", StringComparison.Ordinal)
                ? "ms-resource:" + res
                : $"ms-resource://{c.Name}/{(res.Contains('/') ? res.TrimStart('/') : "Resources/" + res)}";
            return Native.LoadIndirectString($"@{{{c.FullName}?{uri}}}");
        }
        catch { return null; }
    }
}

internal static class GogScanner
{
    private const string GamesPath = @"SOFTWARE\GOG.com\Games";

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        using var games = Reg.Open(RegistryHive.LocalMachine, RegistryView.Registry32, GamesPath);
        if (games == null) return results;

        foreach (var id in games.GetSubKeyNames())
        {
            try
            {
                using var k = games.OpenSubKey(id);
                var path = Paths.NormalizeDir(Reg.Str(k, "path"));
                if (path == null || !Directory.Exists(path)) continue;
                if (!string.IsNullOrWhiteSpace(Reg.Str(k, "dependsOn") ?? Reg.Str(k, "DEPENDSON"))) continue; // DLC

                var gameId = Reg.Str(k, "gameID") ?? id;
                var reg = ctx.Take(gameId + "_is1");
                var name = Reg.Str(k, "gameName") ?? Reg.Str(k, "GAMENAME") ?? reg?.DisplayName ?? Path.GetFileName(path);
                var command = Reg.Str(k, "uninstallCommand") ?? reg?.UninstallString;
                if (command == null)
                {
                    var unins = Path.Combine(path, "unins000.exe");
                    if (File.Exists(unins)) command = $"\"{unins}\"";
                }

                var icons = new List<string> { Path.Combine(path, $"goggame-{gameId}.ico") };
                var exe = Reg.Str(k, "exe");
                if (exe != null) icons.Add(exe);
                if (reg?.DisplayIcon != null) icons.Add(reg.DisplayIcon);

                var keyPath = GamesPath + "\\" + id;
                results.Add(new GameEntry
                {
                    Key = "gog:" + gameId,
                    Name = GameScanner.CleanName(name),
                    Platform = GamePlatform.Gog,
                    InstallPath = path,
                    MeasurePath = path,
                    NeedsMeasure = true,
                    IconSources = icons,
                    Kind = command != null ? UninstallKind.Command : UninstallKind.Uri,
                    UninstallCommand = command,
                    UninstallUri = command == null ? $"goggalaxy://openGameView/{gameId}" : null,
                    StillInstalled = () => Reg.Exists(RegistryHive.LocalMachine, RegistryView.Registry32, keyPath) && Directory.Exists(path),
                });
            }
            catch (Exception ex) { Log.Error("gog " + id, ex); }
        }
        return results;
    }
}

internal static class UbisoftScanner
{
    private const string InstallsPath = @"SOFTWARE\Ubisoft\Launcher\Installs";

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        using var installs = Reg.Open(RegistryHive.LocalMachine, RegistryView.Registry32, InstallsPath);
        if (installs == null) return results;

        foreach (var id in installs.GetSubKeyNames())
        {
            try
            {
                using var k = installs.OpenSubKey(id);
                var dir = Paths.NormalizeDir(Reg.Str(k, "InstallDir"));
                if (dir == null || !Directory.Exists(dir) || Paths.IsProtected(dir, out _)) continue;
                var reg = ctx.Take("Uplay Install " + id);
                var icons = new List<string>();
                if (reg?.DisplayIcon != null) icons.Add(reg.DisplayIcon);
                var keyPath = InstallsPath + "\\" + id;
                results.Add(new GameEntry
                {
                    Key = "ubisoft:" + id,
                    Name = GameScanner.CleanName(reg?.DisplayName ?? Path.GetFileName(dir)),
                    Platform = GamePlatform.Ubisoft,
                    InstallPath = dir,
                    MeasurePath = dir,
                    NeedsMeasure = true,
                    IconSources = icons,
                    Kind = UninstallKind.Uri,
                    UninstallUri = "uplay://uninstall/" + id,
                    StillInstalled = () => Reg.Exists(RegistryHive.LocalMachine, RegistryView.Registry32, keyPath) && Directory.Exists(dir),
                });
            }
            catch (Exception ex) { Log.Error("ubisoft " + id, ex); }
        }
        return results;
    }
}

/// <summary>Jeux HoYoverse installés via HoYoPlay (Genshin Impact, Honkai: Star Rail, ZZZ…).</summary>
internal static class HoYoPlayScanner
{
    private static readonly Dictionary<string, string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hk4e"] = "Genshin Impact",
        ["hkrpg"] = "Honkai: Star Rail",
        ["nap"] = "Zenless Zone Zero",
        ["bh3"] = "Honkai Impact 3rd",
    };

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        foreach (var basePath in new[] { @"Software\Cognosphere\HYP\1_0", @"Software\miHoYo\HYP\1_0" })
        {
            using var b = Registry.CurrentUser.OpenSubKey(basePath);
            if (b == null) continue;
            var launcherDir = Paths.NormalizeDir(Reg.Str(b, "InstallPath"));
            foreach (var biz in b.GetSubKeyNames())
            {
                try
                {
                    using var k = b.OpenSubKey(biz);
                    var path = Paths.NormalizeDir(Reg.Str(k, "GameInstallPath"));
                    if (path == null || !Directory.Exists(path) || Paths.IsProtected(path, out _)) continue;
                    var reg = ctx.TakeWhere(e => e.KeyName.StartsWith(biz + "_", StringComparison.OrdinalIgnoreCase));
                    var prefix = biz.Split('_')[0];
                    var name = reg?.DisplayName ?? (KnownGames.TryGetValue(prefix, out var n) ? n : Path.GetFileName(path));
                    var command = reg?.UninstallString
                                  ?? (launcherDir != null ? $"\"{Path.Combine(launcherDir, "launcher.exe")}\" --uninstall_game={biz}" : null);
                    var icons = new List<string>();
                    var ico = Reg.Str(k, "GameIconPath");
                    if (ico != null) icons.Add(ico);
                    if (reg?.DisplayIcon != null) icons.Add(reg.DisplayIcon);
                    results.Add(new GameEntry
                    {
                        Key = "hoyoplay:" + biz,
                        Name = GameScanner.CleanName(name),
                        Platform = GamePlatform.HoYoPlay,
                        InstallPath = path,
                        MeasurePath = path,
                        NeedsMeasure = true,
                        IconSources = icons,
                        Kind = command != null ? UninstallKind.Command : UninstallKind.None,
                        UninstallCommand = command,
                        StillInstalled = () => (reg?.StillExists() ?? true) && Directory.Exists(path),
                    });
                }
                catch (Exception ex) { Log.Error("hoyoplay " + biz, ex); }
            }
        }
        return results;
    }
}

/// <summary>
/// Jeux reconnus dans « Applications installées » : EA app, Battle.net, Rockstar, Riot, Amazon,
/// et une liste de jeux/launchers autonomes connus.
/// </summary>
internal static class RegistryGamesScanner
{
    // Jeux et lanceurs « tout-en-un » connus (le jeu est installé dans le dossier du lanceur).
    private static readonly Regex KnownOtherNames = new(
        @"^(osu!|eve-online|EVE Online|Hytale|FiveM|RAGE Multiplayer|RageMP|alt:V|Roblox Player|Bloxstrap|Fishstrap|" +
        @"Minecraft Launcher|Warframe|Path of Exile|War Thunder|Albion Online|" +
        @"RuneScape|Old School RuneScape|Jagex Launcher|Guild Wars|FINAL FANTASY XIV|Black Desert|Tibia|" +
        @"Escape from Tarkov|BsgLauncher|Battlestate Games Launcher|RSI Launcher|Star Citizen|Lost Ark|" +
        @"Wuthering Waves|League of Legends|VALORANT|Legends of Runeterra|Genshin Impact|Honkai|Zenless Zone Zero|" +
        @"Dark and Darker|Blacksmith|Brawlhalla|Overwatch|Diablo|Hearthstone|StarCraft|World of Warcraft|Heroes of the Storm|" +
        @"MapleStory|Marvel Rivals|NARAKA|Arena Breakout|CrossFire|Metin2|RAID: Shadow Legends|Mortal Online|" +
        @"Lineage|Ragnarok|Dofus|Wakfu|Mabinogi|Vindictus|Elsword|Blade & Soul|Perfect World|Neverwinter|" +
        @"Star Trek Online|Phantasy Star Online|Tower of Fantasy|Snowbreak|Punishing: Gray Raven|Blue Archive|Arknights)" +
        @"(?=$|[^\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Éditeurs de jeux / lanceurs qui inscrivent chaque jeu dans « Applications installées ».
    private static readonly string[] KnownOtherPublishers =
    {
        "ppy Pty", "CCP ehf", "Hypixel Studios", "Cfx.re", "Roblox Corporation", "Digital Extremes", "Grinding Gear Games",
        "Gaijin", "Sandbox Interactive", "Jagex", "ArenaNet", "Pearl Abyss", "CipSoft", "Battlestate Games",
        "Cloud Imperium", "Kuro Games", "IRONMACE", "Mojang", "Legacy Games", "Big Fish Games", "Gameforge", "NEXON",
        "Garena", "Smilegate", "Proxima Beta", "Level Infinite", "Plarium", "Kakao Games", "NCSOFT", "Ankama",
        "Arc Games", "Gearbox", "Square Enix", "Bandai Namco", "SEGA", "CAPCOM", "KONAMI",
    };

    // Les lanceurs eux-mêmes ne sont pas des jeux (sauf ceux de la liste de noms ci-dessus).
    private static readonly Regex LauncherLike = new(
        @"(Launcher|Client|Game ?Center|Play|Uninstaller|Updater|Redistributable|Runtime|Anti-?Cheat|SDK|Tools?|Editor|Server)\s*$|^(PURPLE|Garena|Plarium Play)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<GameEntry> Scan(ScanContext ctx)
    {
        var results = new List<GameEntry>();
        foreach (var e in ctx.Remaining.ToList())
        {
            var platform = Classify(e);
            if (platform == null) continue;
            ctx.Consume(e);

            var path = Paths.NormalizeDir(e.InstallLocation);
            if (path != null && (Paths.IsProtected(path, out _) || !Directory.Exists(path))) path = null;
            path ??= GameScanner.DirFromIcon(e.DisplayIcon);
            bool exists = path != null && Directory.Exists(path);
            var icons = new List<string>();
            if (e.DisplayIcon != null) icons.Add(e.DisplayIcon);
            var finalPath = path;

            results.Add(new GameEntry
            {
                Key = $"reg:{e.Hive}:{e.View}:{e.KeyName}",
                Name = GameScanner.CleanName(e.DisplayName),
                Platform = platform.Value,
                InstallPath = path,
                MeasurePath = exists ? path : null,
                NeedsMeasure = exists,
                SizeBytes = e.EstimatedSizeKb is > 0 ? e.EstimatedSizeKb * 1024 : null,
                SizeIsApprox = true,
                IconSources = icons,
                Kind = UninstallKind.Command,
                UninstallCommand = e.UninstallString,
                StillInstalled = () => e.StillExists() && (finalPath == null || Directory.Exists(finalPath)),
            });
        }
        return results;
    }

    /// <summary>
    /// Pour les éditeurs aux catalogues mixtes (SEGA, CAPCOM, Valve…) : on exige un vrai dossier de jeu,
    /// pas un outil installé dans un dossier système ou sans emplacement connu.
    /// </summary>
    private static bool LooksLikeGameInstall(UninstallEntry e)
    {
        var dir = Paths.NormalizeDir(e.InstallLocation) ?? GameScanner.DirFromIcon(e.DisplayIcon);
        return dir != null && Directory.Exists(dir) && !Paths.IsProtected(dir, out _);
    }

    internal static GamePlatform? Classify(UninstallEntry e)
    {
        var us = e.UninstallString;
        var dn = e.DisplayName;
        var pub = e.Publisher ?? "";
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;

        if (us.Contains("Blizzard Uninstaller", ic) && us.Contains("--uid=", ic)
            && !Regex.IsMatch(us, @"--uid=(battle\.net|bna|agent)\b", RegexOptions.IgnoreCase))
            return GamePlatform.BattleNet;

        if ((us.Contains(@"\EAInstaller\", ic) || (us.Contains("Cleanup.exe", ic) && us.Contains("uninstall_game", ic)))
            && !dn.Equals("EA app", ic) && !dn.StartsWith("Origin", ic))
            return GamePlatform.EA;

        if (us.Contains("RiotClientServices", ic) && us.Contains("--uninstall-product=", ic)
            && !us.Contains("--uninstall-product=Riot_Client", ic))
            return GamePlatform.Riot;

        if (pub.StartsWith("Rockstar Games", ic) && us.Contains("-uninstall=", ic)
            && !us.Contains("-uninstall=launcher", ic) && !dn.Contains("Social Club", ic) && !dn.Contains("Launcher", ic))
            return GamePlatform.Rockstar;

        if (us.Contains("Amazon Game Remover", ic))
            return GamePlatform.Amazon;

        // Ubisoft / GOG / Steam quand leur registre principal est absent
        if (e.KeyName.StartsWith("Uplay Install ", ic) || us.Contains("uplay://uninstall/", ic))
            return GamePlatform.Ubisoft;
        if (e.KeyName.EndsWith("_is1", ic) && (pub.StartsWith("GOG", ic) || (e.InstallLocation?.Contains("GOG Games", ic) ?? false)))
            return GamePlatform.Gog;

        // Wargaming Game Center (World of Tanks, World of Warships…)
        if ((us.Contains("Wargaming.net", ic) || us.Contains("wgc.exe", ic) || us.Contains("wgc_api", ic) || pub.StartsWith("Wargaming", ic)
             || Regex.IsMatch(dn, @"^World of (Tanks|Warships|Warplanes)", RegexOptions.IgnoreCase))
            && !LauncherLike.IsMatch(dn))
            return GamePlatform.Wargaming;

        if (KnownOtherNames.IsMatch(dn))
            return GamePlatform.Other;
        if (KnownOtherPublishers.Any(p => pub.StartsWith(p, ic)) && !LauncherLike.IsMatch(dn) && LooksLikeGameInstall(e))
            return GamePlatform.Other;

        return null;
    }
}
