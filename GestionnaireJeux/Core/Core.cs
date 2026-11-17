using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace GestionnaireJeux;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Formats dans la langue de l'interface (tailles, dates relatives, pluriels).</summary>
public static class Fmt
{
    private static readonly string[] UnitsFr = { "o", "Ko", "Mo", "Go", "To" };
    private static readonly string[] UnitsEn = { "B", "KB", "MB", "GB", "TB" };

    public static string Size(long bytes)
    {
        if (bytes < 0) bytes = 0;
        var units = Loc.Instance.IsFrench ? UnitsFr : UnitsEn;
        var culture = Loc.Instance.Culture;
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        string num = i == 0 ? v.ToString("0", culture) : v < 100 ? v.ToString("0.0", culture) : v.ToString("0", culture);
        return num + "\u00A0" + units[i];
    }

    /// <summary>« 3 jeux » / « 3 games » à partir des clés de traduction du singulier et du pluriel.</summary>
    public static string Plural(int n, string oneKey, string manyKey)
    {
        bool plural = Loc.Instance.IsFrench ? n > 1 : n != 1;
        return $"{n}\u00A0{Loc.T(plural ? manyKey : oneKey)}";
    }

    public static string Relative(DateTime when)
    {
        var days = (DateTime.Now.Date - when.Date).TotalDays;
        if (days <= 0) return Loc.T("Today");
        if (days < 2) return Loc.T("Yesterday");
        if (days < 7) return Loc.F("DaysAgo", (int)days);
        if (days < 30) { int w = (int)(days / 7); return w <= 1 ? Loc.T("WeekAgo") : Loc.F("WeeksAgo", w); }
        if (days < 365) { int m = Math.Max(1, (int)(days / 30.44)); return m == 1 ? Loc.T("MonthAgo") : Loc.F("MonthsAgo", m); }
        int y = (int)(days / 365.25);
        return y <= 1 ? Loc.T("YearAgo") : Loc.F("YearsAgo", y);
    }
}

public static class Paths
{
    /// <summary>Nettoie un chemin de dossier (guillemets, slashs, variables d'environnement, slash final).</summary>
    public static string? NormalizeDir(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"').Trim()).Replace('/', '\\');
        if (s.Length == 0) return null;
        try { s = Path.GetFullPath(s); } catch { return null; }
        var trimmed = s.TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    /// <summary>"E:\" pour "E:\Jeux\X" ; chaîne vide si inconnu.</summary>
    public static string DriveRootOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) ? "" : root.TrimEnd('\\').ToUpperInvariant() + "\\";
        }
        catch { return ""; }
    }

    public static bool IsDriveRoot(string path)
    {
        var full = path.TrimEnd('\\');
        var root = DriveRootOf(path).TrimEnd('\\');
        return full.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string>? _protectedDirs;

    private static IReadOnlyList<string> ProtectedDirs => _protectedDirs ??= BuildProtected();

    private static List<string> BuildProtected()
    {
        var list = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p)) list.Add(p.TrimEnd('\\')); }
        var folders = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.CommonDocuments, Environment.SpecialFolder.Programs, Environment.SpecialFolder.StartMenu,
        };
        foreach (var f in folders) Add(Environment.GetFolderPath(f));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Path.GetDirectoryName(profile)); // C:\Users
        Add(Environment.GetEnvironmentVariable("ProgramW6432"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
        Add(Path.Combine(profile, "Downloads"));
        return list;
    }

    /// <summary>
    /// Vrai si supprimer (ou mesurer) ce dossier serait dangereux : racine de lecteur,
    /// dossier système, ou parent d'un dossier système.
    /// </summary>
    public static bool IsProtected(string? path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) { reason = Loc.T("ReasonEmpty"); return true; }
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { reason = Loc.T("ReasonInvalid"); return true; }
        if (!Path.IsPathFullyQualified(full + "\\")) { reason = Loc.T("ReasonRelative"); return true; }
        if (IsDriveRoot(full)) { reason = Loc.T("ReasonRoot"); return true; }
        foreach (var p in ProtectedDirs)
        {
            if (full.Equals(p, StringComparison.OrdinalIgnoreCase)) { reason = Loc.F("ReasonSystem", p); return true; }
            if (p.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) { reason = Loc.F("ReasonContainsSystem", p); return true; }
        }
        return false;
    }
}

/// <summary>Référence d'icône du registre : "C:\x\jeu.exe",0 → (chemin, index).</summary>
public static class IconRef
{
    private static readonly Regex IndexSuffix = new(@"^(.*?)\s*,\s*(-?\d+)$", RegexOptions.Compiled);

    public static (string? Path, int Index) Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, 0);
        var s = Environment.ExpandEnvironmentVariables(raw.Trim());
        int index = 0;
        var m = IndexSuffix.Match(s);
        if (m.Success)
        {
            s = m.Groups[1].Value;
            index = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        s = s.Trim().Trim('"').Trim().Replace('/', '\\');
        return (s.Length == 0 ? null : s, index);
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionnaireJeux");

    public static string FilePath => Path.Combine(Folder, "journal.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { /* le journal ne doit jamais faire planter l'appli */ }
    }

    public static void Error(string context, Exception ex) => Write($"ERREUR ({context}) : {ex}");
}

internal static class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[]? phicon, uint[]? piconid, uint nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, StringBuilder lpszFilePath,
        uint cchFilePath, uint dwFlags);

    /// <summary>
    /// Chemin réel d'un dossier, jonctions/liens résolus et casse corrigée
    /// (ex. C:\Program Files\WindowsApps\Jeu → E:\XboxGames\Jeu\Content).
    /// </summary>
    public static string? GetFinalPath(string path)
    {
        try
        {
            const uint shareAll = 0x1 | 0x2 | 0x4;        // lecture | écriture | suppression
            const uint openExisting = 3;
            const uint backupSemantics = 0x02000000;      // nécessaire pour ouvrir un dossier
            using var h = CreateFileW(path, 0, shareAll, IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
            if (h.IsInvalid) return null;
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return null;
            var s = sb.ToString();
            if (s.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + s[8..];
            return s.StartsWith(@"\\?\", StringComparison.Ordinal) ? s[4..] : s;
        }
        catch { return null; }
    }

    /// <summary>Résout une chaîne "@{Paquet?ms-resource://…}" (noms des applis du Store).</summary>
    public static string? LoadIndirectString(string source)
    {
        try
        {
            var sb = new StringBuilder(1024);
            return SHLoadIndirectString(source, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }

    public static void SetTitleBar(IntPtr hwnd, bool dark, int captionColorRef)
    {
        try
        {
            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));    // DWMWA_USE_IMMERSIVE_DARK_MODE
            int color = captionColorRef;
            DwmSetWindowAttribute(hwnd, 35, ref color, sizeof(int)); // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch { }
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible si la valeur est non nulle (ou chaîne non vide).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is string s ? !string.IsNullOrWhiteSpace(s) : value != null;
        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
