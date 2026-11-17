using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace GestionnaireJeux;

/// <summary>
/// Textes de l'interface en français et en anglais.
/// XAML : Text="{Binding [Clé], Source={x:Static local:Loc.Instance}}" ; code : Loc.T("Clé"), Loc.F("Clé", args).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Language { get; private set; } = "fr";
    public bool IsFrench => Language == "fr";
    public CultureInfo Culture => IsFrench ? Fr : En;

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);
    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance.Get(key), args);

    public static string DefaultLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";

    public void SetLanguage(string? language)
    {
        var lang = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en"
                 : string.Equals(language, "fr", StringComparison.OrdinalIgnoreCase) ? "fr"
                 : DefaultLanguage();
        if (lang == Language && _initialized) return;
        _initialized = true;
        Language = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    private bool _initialized;

    private string Get(string key) =>
        Strings.TryGetValue(key, out var s) ? (IsFrench ? s.Fr : s.En) : key;

    private static readonly Dictionary<string, (string Fr, string En)> Strings = new()
    {
        // Fenêtre principale
        ["AppTitle"] = ("Gestionnaire de jeux", "Game Manager"),
        ["HeaderTitle"] = ("Mes jeux installés", "My installed games"),
        ["Scanning"] = ("Analyse en cours…", "Scanning…"),
        ["NoGames"] = ("Aucun jeu détecté", "No games found"),
        ["Summary"] = ("{0} · {1} au total · sur {2}", "{0} · {1} in total · on {2}"),
        ["game"] = ("jeu", "game"),
        ["games"] = ("jeux", "games"),
        ["drive"] = ("lecteur", "drive"),
        ["drives"] = ("lecteurs", "drives"),
        ["SearchingGames"] = ("Recherche des jeux…", "Looking for games…"),
        ["MeasuringOne"] = ("Calcul des tailles… (1 restant)", "Calculating sizes… (1 left)"),
        ["MeasuringMany"] = ("Calcul des tailles… ({0} restants)", "Calculating sizes… ({0} left)"),
        ["Refresh"] = ("Actualiser", "Refresh"),
        ["RefreshTip"] = ("Relancer l’analyse des jeux installés", "Scan installed games again"),
        ["LanguageCode"] = ("FR", "EN"),
        ["LanguageTip"] = ("Switch to English", "Passer en français"),
        ["ThemeTip"] = ("Mode clair / sombre", "Light / dark mode"),
        ["Drives"] = ("LECTEURS", "DRIVES"),
        ["LegendGames"] = ("Jeux", "Games"),
        ["LegendOther"] = ("Autres données", "Other data"),
        ["LegendFree"] = ("Espace libre", "Free space"),
        ["SearchPlaceholder"] = ("Rechercher un jeu…", "Search games…"),
        ["ChipTip"] = ("Filtrer par plateforme (cliquez à nouveau pour retirer le filtre)", "Filter by platform (click again to remove the filter)"),
        ["SortBy"] = ("Trier par", "Sort by"),
        ["SortSize"] = ("Taille", "Size"),
        ["SortName"] = ("Nom", "Name"),
        ["SortPlayed"] = ("Dernière partie", "Last played"),
        ["SortPlatform"] = ("Plateforme", "Platform"),
        ["SortPlayedTip"] = ("Les jeux auxquels vous n’avez pas joué depuis longtemps en premier (Steam uniquement)",
                             "Games you haven’t played for a long time first (Steam only)"),
        ["ColGame"] = ("JEU", "GAME"),
        ["ColGames"] = ("JEUX", "GAMES"),
        ["ColPlatform"] = ("PLATEFORME", "PLATFORM"),
        ["ColDrive"] = ("LECTEUR", "DRIVE"),
        ["ColPlayed"] = ("DERNIÈRE PARTIE", "LAST PLAYED"),
        ["ColSize"] = ("TAILLE", "SIZE"),
        ["OpenFolderTip"] = ("Ouvrir le dossier du jeu", "Open the game folder"),
        ["Uninstall"] = ("Désinstaller", "Uninstall"),
        ["UninstallTip"] = ("Désinstaller ce jeu", "Uninstall this game"),
        ["Waiting"] = ("En attente…", "Waiting…"),
        ["Cancel"] = ("Annuler", "Cancel"),
        ["CancelWaitTip"] = ("Arrêter le suivi (si vous avez annulé la désinstallation)", "Stop tracking (if you cancelled the uninstall)"),
        ["Removing"] = ("Suppression…", "Removing…"),
        ["RemovingPct"] = ("Suppression… {0} %", "Removing… {0}%"),
        ["EmptyNoGames"] = ("Aucun jeu détecté sur cet ordinateur.", "No games were found on this computer."),
        ["EmptyFiltered"] = ("Aucun jeu ne correspond à ces filtres.", "No games match these filters."),
        ["LoadingTitle"] = ("Recherche des jeux installés…", "Looking for installed games…"),
        ["BusyRemoving"] = ("Patientez : une suppression est en cours.", "Please wait: a removal is in progress."),
        ["FolderNotFound"] = ("Dossier introuvable.", "Folder not found."),

        // Lecteurs et lignes
        ["AllDrives"] = ("Tous les lecteurs", "All drives"),
        ["NoGame"] = ("Aucun jeu", "No games"),
        ["DrivesFree"] = ("{0} · {1} libres", "{0} · {1} free"),
        ["DriveFree"] = ("{0} libres sur {1}", "{0} free of {1}"),
        ["UnknownSpace"] = ("Espace inconnu", "Unknown space"),
        ["Network"] = ("Réseau", "Network"),
        ["UnknownLocation"] = ("Emplacement inconnu", "Unknown location"),
        ["PlayedAgo"] = ("Joué {0}", "Played {0}"),
        ["Measuring"] = ("Calcul…", "Measuring…"),
        ["Today"] = ("aujourd’hui", "today"),
        ["Yesterday"] = ("hier", "yesterday"),
        ["DaysAgo"] = ("il y a {0} jours", "{0} days ago"),
        ["WeekAgo"] = ("il y a 1 semaine", "1 week ago"),
        ["WeeksAgo"] = ("il y a {0} semaines", "{0} weeks ago"),
        ["MonthAgo"] = ("il y a 1 mois", "1 month ago"),
        ["MonthsAgo"] = ("il y a {0} mois", "{0} months ago"),
        ["YearAgo"] = ("il y a 1 an", "1 year ago"),
        ["YearsAgo"] = ("il y a {0} ans", "{0} years ago"),
        ["Other"] = ("Autres", "Other"),
        ["NoteIncomplete"] = ("Installation incomplète", "Incomplete installation"),

        // Désinstallation
        ["DlgUninstallTitle"] = ("Désinstaller « {0} » ?", "Uninstall “{0}”?"),
        ["DetPlatform"] = ("Plateforme", "Platform"),
        ["DetSize"] = ("Taille", "Size"),
        ["DetLocation"] = ("Emplacement", "Location"),
        ["About"] = ("environ {0}", "about {0}"),
        ["Unknown"] = ("inconnue", "unknown"),
        ["MsgSteam"] = ("Steam va s’ouvrir et vous demander de confirmer. Vos sauvegardes Steam Cloud ne sont pas touchées.",
                        "Steam will open and ask you to confirm. Your Steam Cloud saves are not affected."),
        ["MsgUbisoft"] = ("Ubisoft Connect va s’ouvrir pour confirmer la désinstallation.", "Ubisoft Connect will open to confirm the uninstall."),
        ["MsgUri"] = ("{0} va s’ouvrir pour terminer la désinstallation.", "{0} will open to finish the uninstall."),
        ["MsgCommand"] = ("Le désinstalleur officiel du jeu va se lancer. Windows peut demander une autorisation administrateur.",
                          "The game’s official uninstaller will start. Windows may ask for administrator permission."),
        ["MsgOpenLauncher"] = ("{0} ne permet pas de désinstaller un jeu depuis une autre application : il va s’ouvrir, et il vous suffira de " +
                               "désinstaller le jeu depuis sa bibliothèque. La ligne disparaîtra ici automatiquement.",
                               "{0} can’t uninstall games from another app: it will open, and you just need to uninstall the game " +
                               "from its library. The entry will disappear here automatically."),
        ["MsgAppx"] = ("Windows va désinstaller le jeu, exactement comme depuis l’app Xbox. Vos sauvegardes cloud ne sont pas touchées.",
                       "Windows will uninstall the game, just like the Xbox app does. Your cloud saves are not affected."),
        ["MsgEpic"] = ("Les fichiers du jeu seront supprimés définitivement et le jeu sera retiré d’Epic Games Launcher " +
                       "(fermé automatiquement s’il est ouvert, avec votre accord).",
                       "The game files will be permanently deleted and the game removed from the Epic Games Launcher " +
                       "(closed automatically if it’s running, with your consent)."),
        ["EpicOpen"] = ("Ouvrir Epic Games", "Open Epic Games"),
        ["EpicNote"] = ("Vous préférez passer par Epic ? Choisissez « Ouvrir Epic Games ». Vos sauvegardes cloud ne sont pas touchées.",
                        "Prefer to use Epic? Choose “Open Epic Games”. Your cloud saves are not affected."),
        ["HintSteam"] = ("Confirmez dans la fenêtre de Steam. La ligne disparaîtra une fois le jeu supprimé.",
                         "Confirm in the Steam window. The entry will disappear once the game is removed."),
        ["HintUbisoft"] = ("Confirmez dans Ubisoft Connect. La ligne disparaîtra une fois le jeu supprimé.",
                           "Confirm in Ubisoft Connect. The entry will disappear once the game is removed."),
        ["HintBattleNet"] = ("Suivez le désinstalleur Battle.net.", "Follow the Battle.net uninstaller."),
        ["HintEA"] = ("Suivez les étapes de l’app EA.", "Follow the steps in the EA app."),
        ["HintRiot"] = ("Suivez les étapes du Riot Client.", "Follow the steps in the Riot Client."),
        ["HintHoYoPlay"] = ("Confirmez dans HoYoPlay.", "Confirm in HoYoPlay."),
        ["HintDefault"] = ("Suivez les étapes du désinstalleur. La ligne disparaîtra une fois le jeu supprimé.",
                           "Follow the uninstaller’s steps. The entry will disappear once the game is removed."),
        ["HintLauncher"] = ("Désinstallez « {0} » depuis {1}. La ligne disparaîtra une fois le jeu supprimé.",
                            "Uninstall “{0}” from {1}. The entry will disappear once the game is removed."),
        ["HintEpicLibrary"] = ("Désinstallez le jeu depuis la bibliothèque d’Epic Games (clic droit › Gérer › Désinstaller).",
                               "Uninstall the game from the Epic Games library (right-click › Manage › Uninstall)."),
        ["ErrLaunch"] = ("Impossible de lancer la désinstallation", "Couldn’t start the uninstall"),
        ["AppxRemoving"] = ("Désinstallation par Windows…", "Windows is uninstalling the game…"),
        ["AppxFailedTitle"] = ("Windows n’a pas pu désinstaller ce jeu", "Windows couldn’t uninstall this game"),
        ["UnknownError"] = ("Erreur inconnue.", "Unknown error."),
        ["AppxRetry"] = ("Vous pouvez réessayer depuis l’app Xbox ou les Paramètres Windows.", "You can try again from the Xbox app or Windows Settings."),
        ["OpenSettings"] = ("Ouvrir les Paramètres", "Open Settings"),
        ["EpicRunningTitle"] = ("Epic Games Launcher est ouvert", "Epic Games Launcher is running"),
        ["EpicRunningMsg"] = ("Il doit être fermé pendant la suppression, sinon il risque de réafficher le jeu comme installé. " +
                              "Si un téléchargement est en cours dans Epic, il sera interrompu.",
                              "It must be closed during the removal, otherwise it may show the game as installed again. " +
                              "Any download in progress in Epic will be interrupted."),
        ["EpicClose"] = ("Fermer Epic et continuer", "Close Epic and continue"),
        ["EpicStillTitle"] = ("Epic Games Launcher est toujours ouvert", "Epic Games Launcher is still running"),
        ["EpicStillMsg"] = ("Fermez-le depuis la zone de notification (clic droit sur l’icône › Quitter), puis réessayez.",
                            "Close it from the notification area (right-click the icon › Exit), then try again."),
        ["RemovingFiles"] = ("Suppression des fichiers du jeu…", "Deleting game files…"),
        ["RemoveFailedTitle"] = ("La désinstallation n’a pas pu se terminer", "The uninstall couldn’t finish"),
        ["Uninstalled"] = ("« {0} » a été désinstallé", "“{0}” was uninstalled"),
        ["Freed"] = (" · {0} libérés", " · {0} freed"),
        ["UnexpectedError"] = ("Une erreur inattendue est survenue", "An unexpected error occurred"),
        ["LogDetails"] = ("Détails dans le journal : {0}", "Details in the log: {0}"),
        ["OK"] = ("OK", "OK"),

        // Sécurité et erreurs des services
        ["SafetyRefused"] = ("Suppression refusée par sécurité : {0}.", "Deletion refused for safety: {0}."),
        ["ReasonEmpty"] = ("chemin vide", "empty path"),
        ["ReasonInvalid"] = ("chemin invalide", "invalid path"),
        ["ReasonRelative"] = ("chemin relatif", "relative path"),
        ["ReasonRoot"] = ("racine d’un lecteur", "drive root"),
        ["ReasonSystem"] = ("dossier système ({0})", "system folder ({0})"),
        ["ReasonContainsSystem"] = ("contient un dossier système ({0})", "contains a system folder ({0})"),
        ["ReasonLibrary"] = ("c’est un dossier de bibliothèque, pas celui d’un jeu", "it’s a library folder, not a game folder"),
        ["EpicPartial"] = ("Certains fichiers n’ont pas pu être supprimés (jeu ouvert ? droits insuffisants ?). Le jeu reste listé dans " +
                           "Epic Games Launcher ; vous pouvez réessayer ou ouvrir le dossier.",
                           "Some files couldn’t be deleted (is the game running? missing permissions?). The game is still listed in " +
                           "the Epic Games Launcher; you can try again or open the folder."),
        ["EpicMetaFailed"] = ("Les fichiers du jeu ont été supprimés, mais il n’a pas pu être retiré de la liste d’Epic : {0}",
                              "The game files were deleted, but it couldn’t be removed from Epic’s list: {0}"),
        ["NoUninstallCommand"] = ("Aucune commande de désinstallation connue pour ce jeu.", "No uninstall command is known for this game."),
        ["InvalidPackage"] = ("Nom de paquet invalide.", "Invalid package name."),
    };
}

/// <summary>Thèmes clair et sombre : chaque pinceau nommé est remplacé dans les ressources de l'application.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static event Action? ThemeChanged;

    // Clé → (sombre, clair) en ARGB
    private static readonly Dictionary<string, (uint Dark, uint Light)> Palette = new()
    {
        ["BgBrush"] = (0xFF0E1014, 0xFFF3F5F9),
        ["SurfaceBrush"] = (0xFF15181F, 0xFFFFFFFF),
        ["CardBrush"] = (0xFF171B22, 0xFFFFFFFF),
        ["CardHoverBrush"] = (0xFF1D222B, 0xFFF6F8FC),
        ["LineBrush"] = (0xFF242A34, 0xFFE2E6ED),
        ["LineStrongBrush"] = (0xFF323949, 0xFFCDD3DD),
        ["TextBrush"] = (0xFFE9ECF2, 0xFF161A22),
        ["TextDimBrush"] = (0xFFA3ABBB, 0xFF4E5767),
        ["TextFaintBrush"] = (0xFF6E7689, 0xFF7E8797),
        ["AccentBrush"] = (0xFF5B8CFF, 0xFF3A6CF0),
        ["AccentSoftBrush"] = (0x245B8CFF, 0x1F3A6CF0),
        ["DangerBrush"] = (0xFFF0525A, 0xFFD63A43),
        ["DangerSoftBrush"] = (0x26F0525A, 0x1FD63A43),
        ["WarningBrush"] = (0xFFF5B82E, 0xFFB7791F),
        ["WarningSoftBrush"] = (0x26F5B82E, 0x24F5B82E),
        ["SuccessBrush"] = (0xFF3FCF8E, 0xFF1E9E63),
        ["ControlBrush"] = (0xFF1D222B, 0xFFFFFFFF),
        ["ControlHoverBrush"] = (0xFF262C38, 0xFFF0F3F8),
        ["ControlPressedBrush"] = (0xFF1A1F27, 0xFFE5E9F0),
        ["ControlBorderBrush"] = (0xFF2E3442, 0xFFD3D9E2),
        ["ControlBorderHoverBrush"] = (0xFF3A4254, 0xFFBCC4D0),
        ["InputBrush"] = (0xFF151920, 0xFFFFFFFF),
        ["ChipHoverBrush"] = (0xFF1C212A, 0xFFF1F4F9),
        ["ChipCheckedBrush"] = (0xFF1C2740, 0xFFE6EEFF),
        ["SegmentCheckedBrush"] = (0xFF283041, 0xFFE3EAF8),
        ["SelectedCardBrush"] = (0xFF16213A, 0xFFEAF0FF),
        ["SelectedBorderBrush"] = (0xFF3F5F9F, 0xFF9DB6F5),
        ["IconTileBrush"] = (0xFF1E232C, 0xFFEEF1F6),
        ["TrackBrush"] = (0xFF262C38, 0xFFE1E5EC),
        ["ScrollThumbBrush"] = (0xFF353C4B, 0xFFC5CBD6),
        ["ScrollThumbHoverBrush"] = (0xFF4A5367, 0xFFAEB6C3),
        ["ScrollThumbDragBrush"] = (0xFF5B657A, 0xFF98A1B0),
        ["ToastBrush"] = (0xFF232A36, 0xFFFFFFFF),
        ["ToastBorderBrush"] = (0xFF36435C, 0xFFD3D9E2),
        ["DetailsBrush"] = (0xFF10141A, 0xFFF6F8FB),
        ["DialogBorderBrush"] = (0xFF2C3240, 0xFFD9DEE6),
        ["ToolTipBrush"] = (0xFF232833, 0xFFFFFFFF),
        ["DangerButtonBrush"] = (0xFF241A1E, 0xFFFFF1F1),
        ["DangerButtonBorderBrush"] = (0xFF4E2A31, 0xFFF1C2C5),
        ["DangerButtonTextBrush"] = (0xFFFF8F95, 0xFFC8323A),
        ["BarGamesBrush"] = (0xFF5B8CFF, 0xFF3A6CF0),
        ["BarOtherBrush"] = (0xFF3A4252, 0xFFB8C0CE),
        ["BarFreeBrush"] = (0xFF222731, 0xFFE6EAF0),
    };

    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is not int v || v == 0;
        }
        catch { return true; }
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var resources = Application.Current.Resources;
        foreach (var (key, (d, l)) in Palette)
        {
            var brush = new SolidColorBrush(ToColor(dark ? d : l));
            brush.Freeze();
            resources[key] = brush;
        }
        Platforms.ResetCache();
        ThemeChanged?.Invoke();
    }

    public static Color ColorOf(string key) =>
        Palette.TryGetValue(key, out var c) ? ToColor(IsDark ? c.Dark : c.Light) : Colors.Transparent;

    /// <summary>Couleur de la barre de titre Windows (COLORREF = 0x00BBGGRR) assortie au fond.</summary>
    public static int CaptionColorRef()
    {
        var c = ColorOf("BgBrush");
        return c.R | (c.G << 8) | (c.B << 16);
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}

/// <summary>Préférences enregistrées dans %LOCALAPPDATA%\GestionnaireJeux\settings.json.</summary>
public sealed class AppSettings
{
    public string? Language { get; set; }
    public string? Theme { get; set; }

    private static string FilePath => Path.Combine(Log.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Error("préférences", ex); }
    }
}
