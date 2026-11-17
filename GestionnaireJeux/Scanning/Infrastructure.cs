using System.Text;
using Microsoft.Win32;

namespace GestionnaireJeux.Scanning;

/// <summary>Lecteur minimal du format texte KeyValues de Valve (libraryfolders.vdf, appmanifest_*.acf).</summary>
public sealed class VdfNode
{
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        int i = 0;
        var root = new VdfNode();
        ParseInto(root, text, ref i, topLevel: true);
        return root;
    }

    private static void ParseInto(VdfNode node, string s, ref int i, bool topLevel)
    {
        while (true)
        {
            var key = Next(s, ref i, out bool keyIsBrace);
            if (key == null) return;
            if (keyIsBrace)
            {
                if (key == "}" && !topLevel) return;
                continue;
            }
            var value = Next(s, ref i, out bool valueIsBrace);
            if (value == null) return;
            if (valueIsBrace)
            {
                if (value == "{")
                {
                    var child = new VdfNode();
                    ParseInto(child, s, ref i, topLevel: false);
                    node.Children[key] = child;
                }
                else if (!topLevel) return;
            }
            else
            {
                node.Values[key] = value;
            }
        }
    }

    private static string? Next(string s, ref int i, out bool isBrace)
    {
        isBrace = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            break;
        }
        if (i >= s.Length) return null;

        char ch = s[i];
        if (ch == '{' || ch == '}')
        {
            i++;
            isBrace = true;
            return ch.ToString();
        }
        if (ch == '"')
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                    i += 2;
                    continue;
                }
                sb.Append(s[i++]);
            }
            i++; // guillemet fermant
            return sb.ToString();
        }
        int start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
        return s[start..i];
    }
}

internal static class Reg
{
    public static RegistryKey? Open(RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var b = RegistryKey.OpenBaseKey(hive, view);
            return b.OpenSubKey(path);
        }
        catch { return null; }
    }

    public static bool Exists(RegistryHive hive, RegistryView view, string path)
    {
        using var k = Open(hive, view, path);
        return k != null;
    }

    public static string? Str(RegistryKey? k, string name)
    {
        if (k == null) return null;
        try { return k.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null; }
        catch { return null; }
    }

    public static long? Long(RegistryKey? k, string name)
    {
        if (k == null) return null;
        try
        {
            return k.GetValue(name) switch
            {
                int i => (uint)i,
                long l => l,
                string s when long.TryParse(s, out var v) => v,
                _ => null,
            };
        }
        catch { return null; }
    }
}

/// <summary>Une entrée « Applications installées » de Windows (clé Uninstall du registre).</summary>
public sealed class UninstallEntry
{
    public required RegistryHive Hive { get; init; }
    public required RegistryView View { get; init; }
    public required string KeyName { get; init; }
    public required string DisplayName { get; init; }
    public required string UninstallString { get; init; }
    public string? Publisher { get; init; }
    public string? InstallLocation { get; init; }
    public string? DisplayIcon { get; init; }
    public long? EstimatedSizeKb { get; init; }

    public string KeyPath => UninstallRegistry.UninstallPath + "\\" + KeyName;
    public bool StillExists() => Reg.Exists(Hive, View, KeyPath);
}

public static class UninstallRegistry
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<UninstallEntry> ReadAll()
    {
        var list = new List<UninstallEntry>();
        var sources = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
        };
        foreach (var (hive, view) in sources)
        {
            using var root = Reg.Open(hive, view, UninstallPath);
            if (root == null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    var displayName = Reg.Str(k, "DisplayName");
                    var uninstall = Reg.Str(k, "UninstallString");
                    if (displayName == null || uninstall == null) continue;
                    if (Reg.Long(k, "SystemComponent") == 1) continue;
                    if (Reg.Str(k, "ParentKeyName") != null) continue; // mises à jour
                    list.Add(new UninstallEntry
                    {
                        Hive = hive,
                        View = view,
                        KeyName = name,
                        DisplayName = displayName,
                        UninstallString = uninstall,
                        Publisher = Reg.Str(k, "Publisher"),
                        InstallLocation = Reg.Str(k, "InstallLocation"),
                        DisplayIcon = Reg.Str(k, "DisplayIcon"),
                        EstimatedSizeKb = Reg.Long(k, "EstimatedSize"),
                    });
                }
                catch (Exception ex) { Log.Error("registre " + name, ex); }
            }
        }
        return list;
    }
}

/// <summary>Partagé entre les scanners : chaque entrée du registre n'est attribuée qu'une fois.</summary>
public sealed class ScanContext
{
    private readonly HashSet<UninstallEntry> _consumed = new();

    public ScanContext(List<UninstallEntry> entries) => Uninstall = entries;

    public List<UninstallEntry> Uninstall { get; }

    public IEnumerable<UninstallEntry> Remaining => Uninstall.Where(e => !_consumed.Contains(e));

    public UninstallEntry? Take(string keyName) =>
        TakeWhere(e => e.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));

    public UninstallEntry? TakeWhere(Func<UninstallEntry, bool> predicate)
    {
        var e = Uninstall.FirstOrDefault(x => !_consumed.Contains(x) && predicate(x));
        if (e != null) _consumed.Add(e);
        return e;
    }

    public void Consume(UninstallEntry e) => _consumed.Add(e);
}

public static class GameScanner
{
    public static List<GameEntry> ScanAll()
    {
        var ctx = new ScanContext(SafeRead());
        var all = new List<GameEntry>();

        void Run(string name, Func<ScanContext, IEnumerable<GameEntry>> scanner)
        {
            try { all.AddRange(scanner(ctx)); }
            catch (Exception ex) { Log.Error("scan " + name, ex); }
        }

        // L'ordre compte : en cas de doublon (même dossier), la première source gagne.
        Run("Steam", SteamScanner.Scan);
        Run("Epic", EpicScanner.Scan);
        Run("Xbox", XboxScanner.Scan);
        Run("GOG", GogScanner.Scan);
        Run("Ubisoft", UbisoftScanner.Scan);
        Run("HoYoPlay", HoYoPlayScanner.Scan);
        Run("GRYPHLINK", GryphlinkScanner.Scan);
        Run("Heroic", HeroicScanner.Scan);
        Run("itch.io", ItchScanner.Scan);
        Run("Meta", MetaScanner.Scan);
        Run("Registre", RegistryGamesScanner.Scan);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameEntry>();
        foreach (var g in all)
        {
            if (!seenKeys.Add(g.Key)) continue;
            var path = g.InstallPath?.TrimEnd('\\');
            if (path != null && !Paths.IsDriveRoot(path) && !seenPaths.Add(path)) continue;
            result.Add(g);
        }
        return result;
    }

    private static List<UninstallEntry> SafeRead()
    {
        try { return UninstallRegistry.ReadAll(); }
        catch (Exception ex) { Log.Error("registre", ex); return new List<UninstallEntry>(); }
    }

    internal static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Dossier d'un exécutable référencé par DisplayIcon, s'il est exploitable.</summary>
    internal static string? DirFromIcon(string? displayIcon)
    {
        var (file, _) = IconRef.Parse(displayIcon);
        if (file == null) return null;
        try
        {
            var dir = Paths.NormalizeDir(Path.GetDirectoryName(file));
            return dir != null && Directory.Exists(dir) && !Paths.IsProtected(dir, out _) ? dir : null;
        }
        catch { return null; }
    }

    internal static string CleanName(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
