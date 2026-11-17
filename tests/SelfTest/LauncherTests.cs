using System.IO;
using System.IO.Compression;
using System.Text;
using GestionnaireJeux;
using GestionnaireJeux.Scanning;
using Microsoft.Win32;

/// <summary>Tests des nouveaux détecteurs sur de fausses installations (dossier temporaire, aucune donnée réelle).</summary>
static class LauncherTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "gj-launchers-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            Heroic(root, check);
            Itch(root, check);
            Meta(root, check);
            Gryphlink(root, check);
            Registry(check);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static string Dir(params string[] parts) { var p = Path.Combine(parts); Directory.CreateDirectory(p); return p; }
    static void Touch(string path, int size = 10) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, new byte[size]); }
    static string J(string s) => s.Replace("\\", "\\\\");

    static void Heroic(string root, Action<bool, string> check)
    {
        var cfg = Dir(root, "heroic");
        var epicGame = Dir(root, "Games", "Hades");
        Touch(Path.Combine(epicGame, "Hades.exe"), 5000);
        var gogGame = Dir(root, "Games", "Witcher 3");
        Touch(Path.Combine(gogGame, "bin", "witcher3.exe"));
        var amazonGame = Dir(root, "Games", "Amazon Thing");
        Touch(Path.Combine(amazonGame, "game.exe"));
        Dir(cfg, "legendaryConfig", "legendary");
        File.WriteAllText(Path.Combine(cfg, "legendaryConfig", "legendary", "installed.json"),
            $"{{ \"Min\": {{ \"app_name\": \"Min\", \"title\": \"Hades\", \"install_path\": \"{J(epicGame)}\", \"install_size\": 15000000000, \"executable\": \"Hades.exe\", \"is_dlc\": false }}," +
            $"  \"Dlc1\": {{ \"app_name\": \"Dlc1\", \"title\": \"Hades DLC\", \"install_path\": \"{J(epicGame)}\", \"is_dlc\": true }} }}");
        Dir(cfg, "gog_store");
        File.WriteAllText(Path.Combine(cfg, "gog_store", "installed.json"),
            $"{{ \"installed\": [ {{ \"appName\": \"1207664643\", \"install_path\": \"{J(gogGame)}\", \"is_dlc\": false }} ] }}");
        Dir(cfg, "store_cache");
        File.WriteAllText(Path.Combine(cfg, "store_cache", "gog_library.json"),
            "{ \"games\": [ { \"app_name\": \"1207664643\", \"title\": \"The Witcher 3: Wild Hunt\" } ] }");
        Dir(cfg, "nile_config", "nile");
        File.WriteAllText(Path.Combine(cfg, "nile_config", "nile", "installed.json"),
            $"[ {{ \"id\": \"amzn1.adg.product.x\", \"path\": \"{J(amazonGame)}\" }} ]");

        var games = HeroicScanner.ScanAt(cfg, @"C:\fake\Heroic.exe");
        check(games.Count == 3, $"Heroic : 3 jeux (DLC ignoré) → {games.Count}");
        check(games.Any(g => g.Name == "Hades" && g.SizeBytes == 15000000000), "Heroic Epic : titre et taille");
        check(games.Any(g => g.Name == "The Witcher 3: Wild Hunt"), "Heroic GOG : titre retrouvé dans le cache de bibliothèque");
        check(games.Any(g => g.Name == "Amazon Thing"), "Heroic Amazon : nom du dossier par défaut");
        check(games.All(g => g.Kind == UninstallKind.OpenLauncher && g.UninstallCommand == "\"C:\\fake\\Heroic.exe\""), "Heroic : désinstallation via le lanceur");
        Directory.Delete(epicGame, true);
        check(!games.First(g => g.Name == "Hades").StillInstalled(), "Heroic : suivi de suppression");
    }

    static void Itch(string root, Action<bool, string> check)
    {
        var appData = Dir(root, "itch");
        var defGame = Dir(appData, "apps", "celeste-classic");
        WriteReceipt(defGame, "Celeste Classic");
        var custom = Dir(root, "ItchGames");
        var customGame = Dir(custom, "baba");
        WriteReceipt(customGame, "Baba Is You");
        Dir(custom, "not-a-game");
        File.WriteAllText(Path.Combine(appData, "preferences.json"), $"{{ \"installLocations\": {{ \"x\": {{ \"path\": \"{J(custom)}\" }} }} }}");

        var games = ItchScanner.ScanAt(appData, null);
        check(games.Count == 2, $"itch : 2 jeux (emplacement par défaut + personnalisé) → {games.Count}");
        check(games.Any(g => g.Name == "Celeste Classic") && games.Any(g => g.Name == "Baba Is You"), "itch : titres lus dans les reçus gzip");
        check(games.All(g => g.Kind == UninstallKind.None), "itch sans appli : pas de bouton de désinstallation");
    }

    static void WriteReceipt(string gameDir, string title)
    {
        var path = Path.Combine(Dir(gameDir, ".itch"), "receipt.json.gz");
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes($"{{ \"game\": {{ \"id\": 1, \"title\": \"{title}\" }}, \"files\": [] }}");
        gz.Write(bytes, 0, bytes.Length);
    }

    static void Meta(string root, Action<bool, string> check)
    {
        var lib = Dir(root, "OculusLib");
        Dir(lib, "Manifests");
        Touch(Path.Combine(lib, "Software", "beat-games-beat-saber", "Beat Saber.exe"));
        File.WriteAllText(Path.Combine(lib, "Manifests", "beat-games-beat-saber.json"),
            "{ \"canonicalName\": \"beat-games-beat-saber\", \"launchFile\": \"Beat Saber.exe\", \"isCore\": false }");
        File.WriteAllText(Path.Combine(lib, "Manifests", "beat-games-beat-saber_assets.json"), "{ \"canonicalName\": \"x\" }");
        Dir(lib, "Software", "oculus-runtime");
        File.WriteAllText(Path.Combine(lib, "Manifests", "oculus-runtime.json"), "{ \"canonicalName\": \"oculus-runtime\", \"isCore\": true }");
        File.WriteAllText(Path.Combine(lib, "Manifests", "missing-game.json"), "{ \"canonicalName\": \"missing-game\" }");

        var games = MetaScanner.ScanAt(new[] { lib }, null);
        check(games.Count == 1 && games[0].Name == "Beat Games Beat Saber", $"Meta : 1 jeu, composants Oculus ignorés → {string.Join(", ", games.Select(g => g.Name))}");
    }

    static void Gryphlink(string root, Action<bool, string> check)
    {
        var data = Dir(root, "LocalLow", "Gryphline");
        var game = Dir(root, "Arknights Endfield");
        Touch(Path.Combine(game, "Endfield.exe"));
        Dir(game, "Endfield_Data");
        Dir(game, "launcher_tmp", "x");
        var logs = Dir(data, "d9f71f86325c5b9733268efda40413c8", "logs");
        File.WriteAllText(Path.Combine(logs, "games.log"),
            "[info] cgv OnGameInstallPathChanged m_strUUID:abc, strGameInstallPath:C:\\Program Files\\GRYPHLINK\\games\\Arknights Endfield\n" +
            $"[info] cgv OnGameInstallPathChanged m_strUUID:abc, strGameInstallPath:{game}\n" +
            $"[info] x strGameInstallPath:{game}\\launcher_tmp\\x\n");
        var launcherDir = Dir(root, "GRYPHLINK");
        Touch(Path.Combine(launcherDir, "Launcher.exe"));

        var games = GryphlinkScanner.ScanAt(data, launcherDir, Dir(root, "Roaming", "Gryphline"));
        check(games.Count == 1 && games[0].Name == "Arknights: Endfield" && games[0].InstallPath == game,
            $"GRYPHLINK : jeu retrouvé via les journaux → {string.Join(", ", games.Select(g => g.Name + " @ " + g.InstallPath))}");
        check(games.Count == 1 && games[0].UninstallCommand == $"\"{Path.Combine(launcherDir, "Launcher.exe")}\"", "GRYPHLINK : ouvre le lanceur pour désinstaller");

        // Variante : seul le journal Unity du jeu est disponible
        var data2 = Dir(root, "LocalLow2", "Gryphline");
        File.WriteAllText(Path.Combine(Dir(data2, "Endfield"), "Player.log"),
            $"Mono path[0] = 'x'\n[Subsystems] Discovering subsystems at path {game.Replace('\\', '/')}/Endfield_Data/UnitySubsystems\n");
        var games2 = GryphlinkScanner.ScanAt(data2, null, Path.Combine(root, "none"));
        check(games2.Count == 1 && games2[0].Kind == UninstallKind.None, "GRYPHLINK : détection via Player.log (Unity)");
    }

    static UninstallEntry E(string name, string uninstall, string? publisher = null, string? location = null, string key = "k") => new()
    {
        Hive = RegistryHive.CurrentUser,
        View = RegistryView.Registry64,
        KeyName = key,
        DisplayName = name,
        UninstallString = uninstall,
        Publisher = publisher,
        InstallLocation = location,
    };

    static void Registry(Action<bool, string> check)
    {
        var existing = Path.GetTempPath();
        var gameDir = Directory.CreateDirectory(Path.Combine(existing, "gj-reg-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        try
        {
            void Is(UninstallEntry e, GamePlatform? expected) =>
                check(RegistryGamesScanner.Classify(e) == expected, $"Registre : « {e.DisplayName} » → {RegistryGamesScanner.Classify(e)?.ToString() ?? "ignoré"}");

            Is(E("World of Tanks EU", "\"C:\\ProgramData\\Wargaming.net\\GameCenter\\wgc.exe\" --uninstall --game_id=WOT.EU.PRODUCTION", "Wargaming.net"), GamePlatform.Wargaming);
            Is(E("Wargaming.net Game Center", "\"C:\\ProgramData\\Wargaming.net\\GameCenter\\uninstall.exe\"", "Wargaming.net"), null);
            Is(E("Assassin's Creed", "\"C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\upc.exe\" uplay://uninstall/1", "Ubisoft", key: "Uplay Install 1"), GamePlatform.Ubisoft);
            Is(E("Cyberpunk 2077", "\"D:\\GOG Games\\Cyberpunk 2077\\unins000.exe\"", "GOG.com", key: "1423049311_is1"), GamePlatform.Gog);
            Is(E("RAID: Shadow Legends", "\"C:\\x\\uninstall.exe\"", "Plarium Global Ltd", gameDir), GamePlatform.Other);
            Is(E("Plarium Play", "\"C:\\x\\uninstall.exe\"", "Plarium Global Ltd", gameDir), null);
            Is(E("Nexon Launcher", "\"C:\\x\\uninstall.exe\"", "NEXON Korea", gameDir), null);
            Is(E("MapleStory", "\"C:\\x\\uninstall.exe\"", "NEXON", gameDir), GamePlatform.Other);
            Is(E("Steam", "C:\\Program Files (x86)\\Steam\\uninstall.exe", "Valve Corporation"), null);
            Is(E("SEGA Tools", "\"C:\\x\\uninstall.exe\"", "SEGA", gameDir), null);
            Is(E("Sonic Frontiers", "\"C:\\x\\uninstall.exe\"", "SEGA", null), null); // éditeur mixte sans dossier de jeu : ignoré
            Is(E("Sonic Frontiers", "\"C:\\x\\uninstall.exe\"", "SEGA", gameDir), GamePlatform.Other);
            Is(E("7-Zip 26.02 (x64)", "\"C:\\Program Files\\7-Zip\\Uninstall.exe\"", "Igor Pavlov", gameDir), null);
        }
        finally { Directory.Delete(gameDir, true); }
    }
}
