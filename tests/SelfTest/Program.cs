using System.Diagnostics;
using System.IO;
using GestionnaireJeux;
using GestionnaireJeux.Scanning;
using GestionnaireJeux.Services;

Loc.Instance.SetLanguage(args.Length > 0 ? args[0] : "fr");

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }

// ---------- CommandLine.Split sur de vraies UninstallString ----------
var cases = new (string cmd, string file, string args)[]
{
    ("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=frFR --uid=auks --displayname=\"Call of Duty\"",
     "C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe", "--lang=frFR --uid=auks --displayname=\"Call of Duty\""),
    ("C:\\Program Files\\HoYoPlay\\launcher.exe --uninstall_game=hk4e_global", "C:\\Program Files\\HoYoPlay\\launcher.exe", "--uninstall_game=hk4e_global"),
    ("MsiExec.exe /X{19BC754E-3456-402F-9A1D-2D162A381632}", "MsiExec.exe", "/X{19BC754E-3456-402F-9A1D-2D162A381632}"),
    ("\"C:\\Program Files (x86)\\Steam\\steam.exe\" steam://uninstall/570", "C:\\Program Files (x86)\\Steam\\steam.exe", "steam://uninstall/570"),
    ("C:\\Program Files\\Denuvo Anti-Cheat\\denuvo-anti-cheat-update-service.exe uninstall",
     "C:\\Program Files\\Denuvo Anti-Cheat\\denuvo-anti-cheat-update-service.exe", "uninstall"),
};
foreach (var (cmd, file, expectedArgs) in cases)
{
    var (f, a) = CommandLine.Split(cmd);
    Check(f == file && a == expectedArgs, $"Split [{f}] [{a}]");
}

// ---------- IconRef ----------
Check(IconRef.Parse("\"E:\\Games\\GTA5.exe\"").Path == "E:\\Games\\GTA5.exe", "IconRef guillemets");
Check(IconRef.Parse("C:\\x\\y.exe,0") == ("C:\\x\\y.exe", 0), "IconRef index");
Check(IconRef.Parse("\"C:\\x y\\z.exe\", -101") == ("C:\\x y\\z.exe", -101), "IconRef index négatif");

// ---------- Vdf ----------
var vdf = Vdf.Parse("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"apps\" { \"228980\" \"0\" }\n\t}\n\t\"1\" { \"path\" \"E:\\\\SteamLibrary\" }\n}");
var lf = vdf.Children["libraryfolders"];
Check(lf.Children["0"].Values["path"] == "C:\\Program Files (x86)\\Steam" && lf.Children["1"].Values["path"] == "E:\\SteamLibrary",
    "Vdf chemins échappés");

// ---------- Garde-fous de suppression ----------
string P(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
foreach (var bad in new[]
         {
             "C:\\", "E:\\", P(Environment.SpecialFolder.Windows), P(Environment.SpecialFolder.ProgramFiles),
             P(Environment.SpecialFolder.ProgramFilesX86), P(Environment.SpecialFolder.UserProfile), "C:\\Users",
             P(Environment.SpecialFolder.LocalApplicationData), "C:\\Program Files\\Epic Games", "E:\\SteamLibrary\\steamapps\\common",
         })
    Check(EpicRemover.CheckSafe(bad) != null, $"Refusé : {bad} ({EpicRemover.CheckSafe(bad)})");
Check(EpicRemover.CheckSafe("C:\\Program Files\\Epic Games\\FallGuys") == null, "Autorisé : dossier d'un jeu Epic");

// ---------- Suppression Epic sur un faux jeu (aucune donnée réelle) ----------
var root = Path.Combine(Path.GetTempPath(), "gj-selftest-" + Guid.NewGuid().ToString("N")[..8]);
var game = Path.Combine(root, "Library", "FakeGame");
var outside = Path.Combine(root, "Outside"); // cible d'une jonction : doit survivre
Directory.CreateDirectory(Path.Combine(game, "Binaries", "Win64"));
Directory.CreateDirectory(outside);
File.WriteAllBytes(Path.Combine(game, "Binaries", "Win64", "game.exe"), new byte[300_000]);
File.WriteAllBytes(Path.Combine(game, "data.pak"), new byte[700_000]);
var ro = Path.Combine(game, "readonly.ini");
File.WriteAllText(ro, "x");
File.SetAttributes(ro, FileAttributes.ReadOnly);
File.WriteAllText(Path.Combine(outside, "precious.txt"), "ne pas supprimer");
Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(game, "LinkToOutside")}\" \"{outside}\"")
    { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
Check(File.Exists(Path.Combine(game, "LinkToOutside", "precious.txt")), "Jonction de test créée");

var manifests = Path.Combine(root, "Manifests");
Directory.CreateDirectory(manifests);
var mainItem = Path.Combine(manifests, "AAA.item"); File.WriteAllText(mainItem, "{\"AppName\":\"Fake\"}");
var dlcItem = Path.Combine(manifests, "BBB.item"); File.WriteAllText(dlcItem, "{\"AppName\":\"FakeDlc\"}");
var otherItem = Path.Combine(manifests, "CCC.item"); File.WriteAllText(otherItem, "{\"AppName\":\"Other\"}");
var dat = Path.Combine(root, "LauncherInstalled.dat");
File.WriteAllText(dat, "{\n\t\"InstallationList\": [\n\t\t{ \"InstallLocation\": \"x\", \"AppName\": \"Fake\" },\n" +
                       "\t\t{ \"InstallLocation\": \"y\", \"AppName\": \"Other\" },\n\t\t{ \"InstallLocation\": \"z\", \"AppName\": \"FakeDlc\" }\n\t]\n}");

var reports = new List<double>();
var result = EpicRemover.Remove(new EpicRemovalInfo
{
    InstallPath = game,
    ManifestPaths = new() { mainItem, dlcItem },
    AppNames = new() { "Fake", "FakeDlc" },
    LauncherInstalledPath = dat,
}, 1_000_001, new SyncProgress(reports.Add));

Check(result.Success, "Remove() réussit " + result.Message);
Check(!Directory.Exists(game), "Dossier du jeu supprimé (y compris fichier en lecture seule)");
Check(File.Exists(Path.Combine(outside, "precious.txt")), "La cible de la jonction est intacte");
Check(!File.Exists(mainItem) && !File.Exists(dlcItem) && File.Exists(otherItem), "Manifestes du jeu et du DLC supprimés, autre jeu conservé");
var datText = File.ReadAllText(dat);
Check(!datText.Contains("\"Fake\"") && !datText.Contains("FakeDlc") && datText.Contains("\"Other\""), "LauncherInstalled.dat : seules les entrées du jeu retirées");
Check(reports.Count > 0 && reports[^1] == 1, $"Progression rapportée ({reports.Count} points, dernier = {reports.LastOrDefault()})");

// Sauvegarde créée, puis nettoyée
var backups = Path.Combine(Log.Folder, "Sauvegardes", "Epic");
var mine = Directory.Exists(backups)
    ? Directory.GetDirectories(backups).Where(d => File.Exists(Path.Combine(d, "jeu.txt")) && File.ReadAllText(Path.Combine(d, "jeu.txt")) == game).ToList()
    : new List<string>();
Check(mine.Count == 1 && File.Exists(Path.Combine(mine[0], "AAA.item")) && File.Exists(Path.Combine(mine[0], "LauncherInstalled.dat")),
    "Sauvegarde des métadonnées créée");
foreach (var d in mine) Directory.Delete(d, true);
if (Directory.Exists(backups) && !Directory.EnumerateFileSystemEntries(backups).Any())
{
    Directory.Delete(backups);
    var parent = Path.GetDirectoryName(backups)!;
    if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
}
Directory.Delete(root, true);

// Refus de sécurité : InstallPath = racine d'un lecteur
var refused = EpicRemover.Remove(new EpicRemovalInfo { InstallPath = "C:\\", ManifestPaths = new(), AppNames = new() }, 0, null);
Check(!refused.Success && refused.Message.Length > 0, "Remove() refuse C:\\ : " + refused.Message);

// ---------- Nouveaux lanceurs (fausses installations) ----------
LauncherTests.Run(Check);

Console.WriteLine(fails == 0 ? "\nTOUS LES TESTS PASSENT" : $"\n{fails} ÉCHEC(S)");
return fails;

sealed class SyncProgress(Action<double> a) : IProgress<double>
{
    public void Report(double v) => a(v);
}
