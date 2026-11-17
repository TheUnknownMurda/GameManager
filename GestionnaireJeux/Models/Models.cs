using System.Windows.Media;
using GestionnaireJeux.Services;

namespace GestionnaireJeux;

public enum GamePlatform
{
    Steam, Epic, Xbox, Gog, Ubisoft, EA, BattleNet, Rockstar, Riot, HoYoPlay, Amazon,
    Gryphlink, Itch, Heroic, Meta, Wargaming, Other,
}

public static class Platforms
{
    private static readonly Dictionary<GamePlatform, SolidColorBrush> Brushes = new();
    private static readonly Dictionary<GamePlatform, SolidColorBrush> Tints = new();

    public static string Label(GamePlatform p) => p switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Xbox => "Xbox",
        GamePlatform.Gog => "GOG",
        GamePlatform.Ubisoft => "Ubisoft",
        GamePlatform.EA => "EA",
        GamePlatform.BattleNet => "Battle.net",
        GamePlatform.Rockstar => "Rockstar",
        GamePlatform.Riot => "Riot Games",
        GamePlatform.HoYoPlay => "HoYoPlay",
        GamePlatform.Amazon => "Amazon",
        GamePlatform.Gryphlink => "GRYPHLINK",
        GamePlatform.Itch => "itch.io",
        GamePlatform.Heroic => "Heroic",
        GamePlatform.Meta => "Meta Quest",
        GamePlatform.Wargaming => "Wargaming",
        _ => Loc.T("Other"),
    };

    /// <summary>Couleurs vives sur fond sombre, plus foncées sur fond clair pour rester lisibles.</summary>
    public static Color ColorOf(GamePlatform p)
    {
        var (dark, light) = p switch
        {
            GamePlatform.Steam => (0x66C0F4u, 0x1B78C2u),
            GamePlatform.Epic => (0xD6D8DEu, 0x3A3F4Au),
            GamePlatform.Xbox => (0x5DC95Du, 0x1F8A1Fu),
            GamePlatform.Gog => (0xC07CFFu, 0x8A3FD1u),
            GamePlatform.Ubisoft => (0x8A8FFFu, 0x4A55E0u),
            GamePlatform.EA => (0xFF6B5Bu, 0xE0442Fu),
            GamePlatform.BattleNet => (0x2E8BFFu, 0x0B6BD6u),
            GamePlatform.Rockstar => (0xF5B82Eu, 0xB9800Au),
            GamePlatform.Riot => (0xFF4670u, 0xD81E4Eu),
            GamePlatform.HoYoPlay => (0x4FD1C5u, 0x138F86u),
            GamePlatform.Amazon => (0xFF9F1Cu, 0xC77400u),
            GamePlatform.Gryphlink => (0xC8E64Cu, 0x6F8A00u),
            GamePlatform.Itch => (0xFF7AC6u, 0xD6337Fu),
            GamePlatform.Heroic => (0x9FB4FFu, 0x5466D6u),
            GamePlatform.Meta => (0x3D7BFFu, 0x1F5BD6u),
            GamePlatform.Wargaming => (0xE05A3Au, 0xB8401Fu),
            _ => (0x9AA4B2u, 0x6B7485u),
        };
        var rgb = ThemeManager.IsDark ? dark : light;
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static SolidColorBrush Brush(GamePlatform p)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(p, out var b))
            {
                b = new SolidColorBrush(ColorOf(p));
                b.Freeze();
                Brushes[p] = b;
            }
            return b;
        }
    }

    public static SolidColorBrush Tint(GamePlatform p)
    {
        lock (Tints)
        {
            if (!Tints.TryGetValue(p, out var b))
            {
                var c = ColorOf(p);
                b = new SolidColorBrush(Color.FromArgb(ThemeManager.IsDark ? (byte)0x24 : (byte)0x1C, c.R, c.G, c.B));
                b.Freeze();
                Tints[p] = b;
            }
            return b;
        }
    }

    public static void ResetCache()
    {
        lock (Brushes) Brushes.Clear();
        lock (Tints) Tints.Clear();
    }
}

/// <summary>
/// OpenLauncher : le lanceur n'a pas de commande de désinstallation ; on l'ouvre (UninstallCommand)
/// et l'utilisateur désinstalle depuis sa bibliothèque.
/// </summary>
public enum UninstallKind { None, Uri, Command, EpicDirect, Appx, OpenLauncher }

public enum GameState { Installed, WaitingExternal, Removing }

public sealed class GameEntry : ObservableObject
{
    private string? _driveRoot;
    private long? _sizeBytes;
    private bool _sizeIsApprox;
    private bool _isMeasuring;
    private ImageSource? _icon;
    private GameState _state;
    private Func<string>? _status;
    private double _progress;
    private bool _isIndeterminate;

    public required string Key { get; init; }
    public required string Name { get; init; }
    public required GamePlatform Platform { get; init; }
    /// <summary>Dossier du jeu (affiché, ouvert, et utilisé pour le lecteur).</summary>
    public string? InstallPath { get; init; }
    /// <summary>Dossier dont on calcule la taille.</summary>
    public string? MeasurePath { get; init; }
    public bool NeedsMeasure { get; init; }
    public DateTime? LastPlayed { get; init; }
    public IReadOnlyList<string> IconSources { get; init; } = Array.Empty<string>();
    /// <summary>Clé de traduction d'une remarque (ex. « NoteIncomplete »).</summary>
    public string? Note { get; init; }

    public UninstallKind Kind { get; init; }
    public string? UninstallUri { get; init; }
    public string? UninstallCommand { get; init; }
    public string? PackageFullName { get; init; }
    public EpicRemovalInfo? Epic { get; init; }
    /// <summary>Nom du lanceur à ouvrir (UninstallKind.OpenLauncher).</summary>
    public string? LauncherName { get; init; }
    /// <summary>Renvoie faux une fois le jeu désinstallé (manifeste/clé de registre/dossier disparus).</summary>
    public Func<bool> StillInstalled { get; init; } = static () => true;

    public string DriveRoot => _driveRoot ??= Paths.DriveRootOf(InstallPath);

    public string DriveLabel => DriveRoot.Length == 3 && DriveRoot[1] == ':' ? DriveRoot[..2] : DriveRoot.Length > 0 ? Loc.T("Network") : "?";

    public string PlatformLabel => Platforms.Label(Platform);
    public Brush PlatformBrush => Platforms.Brush(Platform);
    public Brush PlatformTint => Platforms.Tint(Platform);

    public string Initial
    {
        get
        {
            foreach (var c in Name)
                if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            return "?";
        }
    }

    public string InstallPathDisplay => InstallPath ?? Loc.T("UnknownLocation");
    public string LastPlayedText => LastPlayed is DateTime d ? Loc.F("PlayedAgo", Fmt.Relative(d)) : "";
    public DateTime SortLastPlayed => LastPlayed ?? DateTime.MaxValue;
    public bool CanUninstall => Kind != UninstallKind.None;
    public string? NoteText => Note == null ? null : Loc.T(Note);

    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (Set(ref _sizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(SortSize));
            }
        }
    }

    public bool SizeIsApprox
    {
        get => _sizeIsApprox;
        set { if (Set(ref _sizeIsApprox, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public bool IsMeasuring
    {
        get => _isMeasuring;
        set { if (Set(ref _isMeasuring, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public string SizeText => SizeBytes is long b ? (SizeIsApprox ? "≈\u00A0" : "") + Fmt.Size(b) : IsMeasuring ? Loc.T("Measuring") : "—";
    public long SortSize => SizeBytes ?? -1;

    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public GameState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsWaiting));
                OnPropertyChanged(nameof(IsRemoving));
            }
        }
    }

    public bool IsIdle => State == GameState.Installed;
    public bool IsWaiting => State == GameState.WaitingExternal;
    public bool IsRemoving => State == GameState.Removing;

    /// <summary>Message d'état (recalculé à chaque changement de langue).</summary>
    public string? StatusText => _status?.Invoke();
    public Func<string>? StatusFactory => _status;

    public double Progress
    {
        get => _progress;
        private set { if (Set(ref _progress, value)) OnPropertyChanged(nameof(ProgressText)); }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (Set(ref _isIndeterminate, value))
            {
                OnPropertyChanged(nameof(ShowProgressBar));
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public bool ShowProgressBar => !IsIndeterminate;
    public string ProgressText => IsIndeterminate ? Loc.T("Removing") : Loc.F("RemovingPct", (int)Math.Round(Progress * 100));
    public DateTime PendingSince { get; private set; }

    public void SetWaiting(DateTime since, Func<string>? status)
    {
        PendingSince = since;
        SetStatus(status);
        State = GameState.WaitingExternal;
    }

    public void BeginRemoving(Func<string>? status, bool indeterminate)
    {
        SetStatus(status);
        Progress = 0;
        IsIndeterminate = indeterminate;
        State = GameState.Removing;
    }

    public void SetProgress(double p) => Progress = Math.Clamp(p, 0, 1);

    public void ResetState()
    {
        SetStatus(null);
        Progress = 0;
        State = GameState.Installed;
    }

    private void SetStatus(Func<string>? status)
    {
        _status = status;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Après un changement de langue ou de thème : réévalue les textes et couleurs affichés.</summary>
    public void RefreshDisplay()
    {
        foreach (var p in new[]
                 {
                     nameof(SizeText), nameof(LastPlayedText), nameof(PlatformLabel), nameof(PlatformBrush), nameof(PlatformTint),
                     nameof(DriveLabel), nameof(InstallPathDisplay), nameof(NoteText), nameof(ProgressText), nameof(StatusText),
                 })
            OnPropertyChanged(p);
    }
}

public sealed class DriveEntry : ObservableObject
{
    private int _gamesCount;
    private long _gamesBytes;
    private long _totalBytes;
    private long _freeBytes;
    private string _spaceText = "";
    private Brush? _barBrush;
    private readonly string _title = "";

    public bool IsAll { get; init; }
    /// <summary>"E:\" (vide pour « Tous les lecteurs »).</summary>
    public string Root { get; init; } = "";
    public string Title { get => IsAll ? Loc.T("AllDrives") : _title; init => _title = value; }
    public string VolumeLabel { get; init; } = "";
    public string Glyph => IsAll ? "\uE71D" : "\uEDA2";
    public bool HasBar => !IsAll && _totalBytes > 0;

    public int GamesCount { get => _gamesCount; private set => Set(ref _gamesCount, value); }
    public long GamesBytes { get => _gamesBytes; private set => Set(ref _gamesBytes, value); }
    public long FreeBytes => _freeBytes;
    public string GamesText => GamesCount == 0 ? Loc.T("NoGame") : $"{Fmt.Plural(GamesCount, "game", "games")} · {Fmt.Size(GamesBytes)}";
    public string SpaceText { get => _spaceText; private set => Set(ref _spaceText, value); }
    public Brush? BarBrush { get => _barBrush; private set => Set(ref _barBrush, value); }

    public static DriveEntry CreateAll() => new() { IsAll = true };

    public static DriveEntry FromRoot(string root, string? label)
    {
        var d = new DriveEntry
        {
            Root = root,
            Title = root.Length >= 2 && root[1] == ':' ? root[..2] : root.TrimEnd('\\'),
            VolumeLabel = string.IsNullOrWhiteSpace(label) ? "" : label!,
        };
        d.RefreshSpace();
        return d;
    }

    public void RefreshSpace()
    {
        if (IsAll) return;
        try
        {
            var di = new DriveInfo(Root);
            if (di.IsReady)
            {
                _totalBytes = di.TotalSize;
                _freeBytes = di.AvailableFreeSpace;
            }
        }
        catch { /* lecteur débranché ou inaccessible */ }
        OnPropertyChanged(nameof(HasBar));
    }

    public void UpdateStats(IReadOnlyCollection<GameEntry> games, IReadOnlyCollection<DriveEntry> drives)
    {
        var mine = IsAll ? games : games.Where(g => g.DriveRoot.Equals(Root, StringComparison.OrdinalIgnoreCase)).ToList();
        GamesCount = mine.Count;
        GamesBytes = mine.Sum(g => g.SizeBytes ?? 0);
        OnPropertyChanged(nameof(GamesText));
        OnPropertyChanged(nameof(Title));

        if (IsAll)
        {
            var real = drives.Where(d => !d.IsAll).ToList();
            long free = real.Sum(d => d.FreeBytes);
            SpaceText = Loc.F("DrivesFree", Fmt.Plural(real.Count, "drive", "drives"), Fmt.Size(free));
            return;
        }

        if (_totalBytes <= 0)
        {
            SpaceText = Loc.T("UnknownSpace");
            BarBrush = null;
            return;
        }

        SpaceText = Loc.F("DriveFree", Fmt.Size(_freeBytes), Fmt.Size(_totalBytes));
        double gamesFrac = Math.Clamp((double)GamesBytes / _totalBytes, 0, 1);
        double usedFrac = Math.Clamp((double)(_totalBytes - _freeBytes) / _totalBytes, gamesFrac, 1);
        var cGames = ThemeManager.ColorOf("BarGamesBrush");
        var cOther = ThemeManager.ColorOf("BarOtherBrush");
        var cFree = ThemeManager.ColorOf("BarFreeBrush");
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(cGames, 0));
        brush.GradientStops.Add(new GradientStop(cGames, gamesFrac));
        brush.GradientStops.Add(new GradientStop(cOther, gamesFrac));
        brush.GradientStops.Add(new GradientStop(cOther, usedFrac));
        brush.GradientStops.Add(new GradientStop(cFree, usedFrac));
        brush.GradientStops.Add(new GradientStop(cFree, 1));
        brush.Freeze();
        BarBrush = brush;
    }
}

public sealed class PlatformChip : ObservableObject
{
    private int _count;
    private bool _isChecked;

    public required GamePlatform Platform { get; init; }
    public string Label => Platforms.Label(Platform);
    public Brush Brush => Platforms.Brush(Platform);

    public int Count
    {
        get => _count;
        set { if (Set(ref _count, value)) OnPropertyChanged(nameof(IsShown)); }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) OnPropertyChanged(nameof(IsShown)); }
    }

    /// <summary>Masque les plateformes absentes du lecteur sélectionné (sauf si le filtre est actif).</summary>
    public bool IsShown => Count > 0 || IsChecked;

    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Brush));
    }
}
