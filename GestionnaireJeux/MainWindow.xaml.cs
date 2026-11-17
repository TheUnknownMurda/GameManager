using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionnaireJeux.Scanning;
using GestionnaireJeux.Services;

namespace GestionnaireJeux;

public sealed record StartupOptions(
    string? SnapshotPath = null,
    string? DialogSnapshotPath = null,
    string? Drive = null,
    string? Sort = null,
    string? Search = null)
{
    public bool IsSnapshot => SnapshotPath != null || DialogSnapshotPath != null;
}

public partial class MainWindow : Window
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(45);

    private readonly StartupOptions _options;
    private readonly ObservableCollection<GameEntry> _games = new();
    private readonly ObservableCollection<DriveEntry> _drives = new();
    private readonly ObservableCollection<PlatformChip> _chips = new();
    private readonly ListCollectionView? _view;
    private readonly DispatcherTimer _pendingTimer;
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly List<Task> _backgroundTasks = new();
    private CancellationTokenSource? _backgroundCts;
    private string _sortMode = "size";
    private string _search = "";
    private bool _scanning;
    private int _sizesRemaining;

    public MainWindow(StartupOptions options)
    {
        _options = options;
        InitializeComponent();

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_games);
        _view.Filter = FilterGame;
        _view.IsLiveSorting = true;
        ((INotifyCollectionChanged)_view).CollectionChanged += (_, _) => UpdateEmptyState();
        ApplySort();
        GameList.ItemsSource = _view;
        DriveList.ItemsSource = _drives;
        PlatformChips.ItemsSource = _chips;

        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pendingTimer.Tick += (_, _) => CheckPendingUninstalls();
        _pendingTimer.Start();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _statsTimer.Tick += (_, _) => { _statsTimer.Stop(); UpdateStats(); };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };

        Loc.Instance.LanguageChanged += OnLanguageChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeGlyph();
        SummaryText.Text = Loc.T("Scanning");

        if (options.IsSnapshot)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = 0;
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        Loaded += async (_, _) => await ScanAsync(initial: true);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBar();
    }

    protected override void OnClosed(EventArgs e)
    {
        _backgroundCts?.Cancel();
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    // ───────────────────────────── Langue et thème ─────────────────────────────

    private void ApplyTitleBar() =>
        Native.SetTitleBar(new WindowInteropHelper(this).Handle, ThemeManager.IsDark, ThemeManager.CaptionColorRef());

    private void UpdateThemeGlyph() => ThemeGlyph.Text = ThemeManager.IsDark ? "" : ""; // soleil / lune

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        Loc.Instance.SetLanguage(Loc.Instance.IsFrench ? "en" : "fr");
        var s = AppSettings.Load();
        s.Language = Loc.Instance.Language;
        s.Save();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(!ThemeManager.IsDark);
        var s = AppSettings.Load();
        s.Theme = ThemeManager.IsDark ? "dark" : "light";
        s.Save();
    }

    private void OnLanguageChanged()
    {
        foreach (var g in _games) g.RefreshDisplay();
        foreach (var c in _chips) c.RefreshDisplay();
        _view?.Refresh(); // le tri par plateforme dépend du libellé traduit
        UpdateStats();
        UpdateStatusLine();
        UpdateEmptyState();
    }

    private void OnThemeChanged()
    {
        foreach (var g in _games) g.RefreshDisplay();
        foreach (var c in _chips) c.RefreshDisplay();
        UpdateStats(); // barres des lecteurs
        UpdateThemeGlyph();
        ApplyTitleBar();
    }

    // ───────────────────────────── Analyse ─────────────────────────────

    private async Task ScanAsync(bool initial)
    {
        if (_scanning) return;
        if (_games.Any(g => g.IsRemoving))
        {
            ShowToast(Loc.T("BusyRemoving"), success: false);
            return;
        }

        _scanning = true;
        RescanButton.IsEnabled = false;
        _backgroundCts?.Cancel();
        _backgroundCts = new CancellationTokenSource();
        var ct = _backgroundCts.Token;

        var waiting = _games.Where(g => g.IsWaiting).ToDictionary(g => g.Key, g => (g.PendingSince, g.StatusFactory));
        LoadingPanel.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        SetStatus(Loc.T("SearchingGames"));

        List<GameEntry> found;
        try { found = await Task.Run(GameScanner.ScanAll); }
        catch (Exception ex)
        {
            Log.Error("scan", ex);
            found = new List<GameEntry>();
        }

        _games.Clear();
        foreach (var g in found)
        {
            if (waiting.TryGetValue(g.Key, out var w)) g.SetWaiting(w.PendingSince, w.StatusFactory);
            _games.Add(g);
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        _scanning = false;
        RescanButton.IsEnabled = true;
        RebuildDrives();
        RebuildChips();
        UpdateStats();
        UpdateEmptyState();
        StartBackgroundWork(found, ct);
        Log.Write($"Analyse : {found.Count} jeux trouvés");

        if (initial)
        {
            ApplyStartupOptions();
            if (_options.IsSnapshot) await RunSnapshotAsync();
        }
    }

    private void StartBackgroundWork(List<GameEntry> games, CancellationToken ct)
    {
        _backgroundTasks.Clear();
        var dispatcher = Dispatcher;

        // Icônes
        _backgroundTasks.Add(Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(games, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, g =>
                {
                    var img = IconLoader.Load(g.IconSources);
                    if (img != null) dispatcher.InvokeAsync(() => g.Icon = img);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("icônes", ex); }
        }));

        // Tailles : un fil par lecteur (lire deux dossiers en même temps sur le même disque ralentit tout)
        var toMeasure = games.Where(g => g.NeedsMeasure && g.MeasurePath != null).ToList();
        _sizesRemaining = toMeasure.Count;
        foreach (var g in toMeasure) g.IsMeasuring = true;
        UpdateStatusLine();

        foreach (var group in toMeasure.GroupBy(g => g.DriveRoot, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.OrderByDescending(g => g.SizeBytes ?? 0).ToList();
            _backgroundTasks.Add(Task.Run(() =>
            {
                foreach (var g in list)
                {
                    if (ct.IsCancellationRequested) return;
                    long? size;
                    try { size = FolderSize.TryMeasure(g.MeasurePath!, ct); }
                    catch (OperationCanceledException) { return; }
                    dispatcher.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        if (size is long s)
                        {
                            g.SizeBytes = s;
                            g.SizeIsApprox = false;
                        }
                        g.IsMeasuring = false;
                        _sizesRemaining--;
                        UpdateStatusLine();
                        ScheduleStats();
                    });
                }
            }));
        }
    }

    private void UpdateStatusLine()
    {
        if (_scanning) return;
        SetStatus(_sizesRemaining <= 0 ? "" : _sizesRemaining == 1 ? Loc.T("MeasuringOne") : Loc.F("MeasuringMany", _sizesRemaining));
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ───────────────────────────── Lecteurs, filtres, tri ─────────────────────────────

    private DriveEntry? SelectedDrive => DriveList.SelectedItem as DriveEntry;

    private void RebuildDrives()
    {
        var previous = SelectedDrive?.Root;
        var roots = _games.Select(g => g.DriveRoot).Where(r => r.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _drives.Clear();
        _drives.Add(DriveEntry.CreateAll());
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                // Disques fixes toujours ; amovibles/réseau seulement s'ils contiennent des jeux.
                if (di.DriveType != DriveType.Fixed && !roots.Contains(di.Name)) continue;
                if (!di.IsReady) continue;
                _drives.Add(DriveEntry.FromRoot(di.Name.ToUpperInvariant(), di.VolumeLabel));
            }
            catch { /* lecteur inaccessible */ }
        }
        foreach (var r in roots)
            if (!_drives.Any(d => d.Root.Equals(r, StringComparison.OrdinalIgnoreCase)))
                _drives.Add(DriveEntry.FromRoot(r, null));

        DriveList.SelectedItem = _drives.FirstOrDefault(d => !d.IsAll && d.Root.Equals(previous, StringComparison.OrdinalIgnoreCase))
                                 ?? _drives[0];
    }

    private void RebuildChips()
    {
        var checkedBefore = _chips.Where(c => c.IsChecked).Select(c => c.Platform).ToHashSet();
        _chips.Clear();
        foreach (var grp in _games.GroupBy(g => g.Platform).OrderByDescending(g => g.Count()).ThenBy(g => Platforms.Label(g.Key)))
            _chips.Add(new PlatformChip { Platform = grp.Key, IsChecked = checkedBefore.Contains(grp.Key) });
    }

    private void UpdateStats()
    {
        long total = 0;
        foreach (var g in _games) total += g.SizeBytes ?? 0;
        int driveCount = _games.Select(g => g.DriveRoot).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        SummaryText.Text = _games.Count == 0
            ? Loc.T(_scanning ? "Scanning" : "NoGames")
            : Loc.F("Summary", Fmt.Plural(_games.Count, "game", "games"), Fmt.Size(total), Fmt.Plural(driveCount, "drive", "drives"));

        foreach (var d in _drives) d.UpdateStats(_games, _drives);

        var sel = SelectedDrive;
        foreach (var c in _chips)
            c.Count = _games.Count(g => g.Platform == c.Platform && MatchesDrive(g, sel));

        UpdateListCount();
    }

    private void ScheduleStats()
    {
        if (!_statsTimer.IsEnabled) _statsTimer.Start();
    }

    private void UpdateListCount()
    {
        if (_view == null) return;
        int shown = _view.Count;
        long size = 0;
        foreach (var o in _view) if (o is GameEntry g) size += g.SizeBytes ?? 0;
        ListCountText.Text = shown == 0
            ? Loc.T("ColGames")
            : $"{Fmt.Plural(shown, "ColGame", "ColGames")} · {Fmt.Size(size).ToUpper(Loc.Instance.Culture)}";
    }

    private static bool MatchesDrive(GameEntry g, DriveEntry? d) =>
        d == null || d.IsAll || g.DriveRoot.Equals(d.Root, StringComparison.OrdinalIgnoreCase);

    private bool FilterGame(object o)
    {
        if (o is not GameEntry g) return false;
        if (!MatchesDrive(g, SelectedDrive)) return false;
        bool anyChip = false, chipMatch = false;
        foreach (var c in _chips)
        {
            if (!c.IsChecked) continue;
            anyChip = true;
            if (c.Platform == g.Platform) { chipMatch = true; break; }
        }
        if (anyChip && !chipMatch) return false;
        if (_search.Length > 0)
        {
            var cmp = CultureInfo.InvariantCulture.CompareInfo;
            const CompareOptions opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            if (cmp.IndexOf(g.Name, _search, opts) < 0
                && cmp.IndexOf(g.PlatformLabel, _search, opts) < 0
                && (g.InstallPath == null || cmp.IndexOf(g.InstallPath, _search, opts) < 0))
                return false;
        }
        return true;
    }

    private void RefreshView()
    {
        _view?.Refresh();
        UpdateListCount();
        UpdateEmptyState();
    }

    private void ApplySort()
    {
        if (_view == null) return;
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();
            _view.LiveSortingProperties.Clear();
            switch (_sortMode)
            {
                case "name":
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.Name), ListSortDirection.Ascending));
                    break;
                case "played":
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.SortLastPlayed), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.SortSize), ListSortDirection.Descending));
                    break;
                case "platform":
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.PlatformLabel), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.SortSize), ListSortDirection.Descending));
                    break;
                default:
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.SortSize), ListSortDirection.Descending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(GameEntry.Name), ListSortDirection.Ascending));
                    break;
            }
            foreach (var sd in _view.SortDescriptions) _view.LiveSortingProperties.Add(sd.PropertyName);
        }
    }

    private void UpdateEmptyState()
    {
        if (_view == null || EmptyState == null) return;
        bool show = !_scanning && LoadingPanel.Visibility != Visibility.Visible && _view.IsEmpty;
        EmptyState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T(_games.Count == 0 ? "EmptyNoGames" : "EmptyFiltered");
    }

    private void ApplyStartupOptions()
    {
        if (_options.Drive is string drive)
        {
            var d = _drives.FirstOrDefault(x => !x.IsAll && x.Title.Equals(drive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (d != null) DriveList.SelectedItem = d;
        }
        if (_options.Sort is string sort)
        {
            foreach (var rb in new[] { SortSize, SortName, SortPlayed, SortPlatform })
                if (sort.Equals(rb.Tag as string, StringComparison.OrdinalIgnoreCase)) rb.IsChecked = true;
        }
        if (_options.Search is string search) SearchBox.Text = search;
    }

    // ───────────────────────────── Événements UI ─────────────────────────────

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync(initial: false);

    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view == null) return;
        RefreshView();
        UpdateStats();
    }

    private void Chip_Click(object sender, RoutedEventArgs e) => RefreshView();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        RefreshView();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) SearchBox.Text = "";
    }

    private void Sort_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mode })
        {
            _sortMode = mode;
            ApplySort();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameEntry g) return;
        if (g.InstallPath != null && Directory.Exists(g.InstallPath))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{g.InstallPath}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("explorer", ex); }
        }
        else
        {
            ShowToast(Loc.T("FolderNotFound"), success: false);
        }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameEntry g) await UninstallAsync(g);
    }

    private async void GameList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && GameList.SelectedItem is GameEntry g)
        {
            e.Handled = true;
            await UninstallAsync(g);
        }
    }

    private void CancelWait_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameEntry g) g.ResetState();
    }

    // ───────────────────────────── Désinstallation ─────────────────────────────

    private static List<(string, string)> Details(GameEntry g) => new()
    {
        (Loc.T("DetPlatform"), g.PlatformLabel),
        (Loc.T("DetSize"), g.SizeBytes is long s ? (g.SizeIsApprox ? Loc.F("About", Fmt.Size(s)) : Fmt.Size(s)) : Loc.T("Unknown")),
        (Loc.T("DetLocation"), g.InstallPathDisplay),
    };

    private async Task UninstallAsync(GameEntry g)
    {
        if (!g.IsIdle || !g.CanUninstall) return;

        var (message, secondary, note) = DescribeUninstall(g);
        var choice = ConfirmDialog.Ask(this, Loc.F("DlgUninstallTitle", g.Name), message, Details(g), Loc.T("Uninstall"), secondary, note);
        if (choice == DialogChoice.Cancel) return;

        try
        {
            switch (g.Kind)
            {
                case UninstallKind.Uri:
                case UninstallKind.Command:
                case UninstallKind.OpenLauncher:
                    Uninstaller.LaunchExternal(g);
                    g.SetWaiting(DateTime.Now, () => WaitingHint(g));
                    Log.Write($"Désinstalleur lancé pour {g.Name} ({g.PlatformLabel})");
                    break;

                case UninstallKind.Appx:
                    await RemoveAppxAsync(g);
                    break;

                case UninstallKind.EpicDirect when choice == DialogChoice.Secondary:
                    Uninstaller.OpenUri(EpicRemover.LibraryUri);
                    g.SetWaiting(DateTime.Now, () => Loc.T("HintEpicLibrary"));
                    break;

                case UninstallKind.EpicDirect:
                    await RemoveEpicAsync(g);
                    break;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            g.ResetState(); // autorisation administrateur refusée
        }
        catch (Exception ex)
        {
            g.ResetState();
            Log.Error("désinstallation " + g.Name, ex);
            ConfirmDialog.Error(this, Loc.T("ErrLaunch"), ex.Message);
        }
    }

    private static (string Message, string? Secondary, string? Note) DescribeUninstall(GameEntry g) => g.Kind switch
    {
        UninstallKind.Uri when g.Platform == GamePlatform.Steam => (Loc.T("MsgSteam"), null, null),
        UninstallKind.Uri when g.Platform == GamePlatform.Ubisoft => (Loc.T("MsgUbisoft"), null, null),
        UninstallKind.Uri => (Loc.F("MsgUri", g.PlatformLabel), null, null),
        UninstallKind.Command => (Loc.T("MsgCommand"), null, null),
        UninstallKind.OpenLauncher => (Loc.F("MsgOpenLauncher", g.LauncherName), null, null),
        UninstallKind.Appx => (Loc.T("MsgAppx"), null, null),
        UninstallKind.EpicDirect => (Loc.T("MsgEpic"), Loc.T("EpicOpen"), Loc.T("EpicNote")),
        _ => ("", null, null),
    };

    private static string WaitingHint(GameEntry g) => g.Kind == UninstallKind.OpenLauncher
        ? Loc.F("HintLauncher", g.Name, g.LauncherName)
        : g.Platform switch
        {
            GamePlatform.Steam => Loc.T("HintSteam"),
            GamePlatform.Ubisoft => Loc.T("HintUbisoft"),
            GamePlatform.BattleNet => Loc.T("HintBattleNet"),
            GamePlatform.EA => Loc.T("HintEA"),
            GamePlatform.Riot => Loc.T("HintRiot"),
            GamePlatform.HoYoPlay => Loc.T("HintHoYoPlay"),
            _ => Loc.T("HintDefault"),
        };

    private async Task RemoveAppxAsync(GameEntry g)
    {
        g.BeginRemoving(() => Loc.T("AppxRemoving"), indeterminate: true);
        var (ok, error) = await Uninstaller.RemoveAppxAsync(g.PackageFullName!);
        bool still;
        try { still = g.StillInstalled(); } catch { still = true; }
        if (ok || !still)
        {
            OnGameRemoved(g);
            return;
        }
        g.ResetState();
        Log.Write($"Échec Remove-AppxPackage {g.PackageFullName} : {error}");
        var choice = ConfirmDialog.Ask(this, Loc.T("AppxFailedTitle"),
            (string.IsNullOrWhiteSpace(error) ? Loc.T("UnknownError") : error) + "\n\n" + Loc.T("AppxRetry"),
            null, Loc.T("OpenSettings"), kind: DialogKind.Error);
        if (choice == DialogChoice.Primary) Uninstaller.OpenUri("ms-settings:appsfeatures");
    }

    private async Task RemoveEpicAsync(GameEntry g)
    {
        var info = g.Epic!;
        if (EpicRemover.IsLauncherRunning())
        {
            var c = ConfirmDialog.Ask(this, Loc.T("EpicRunningTitle"), Loc.T("EpicRunningMsg"), null, Loc.T("EpicClose"), kind: DialogKind.Warning);
            if (c != DialogChoice.Primary) return;
            if (!await EpicRemover.CloseLauncherAsync())
            {
                ConfirmDialog.Error(this, Loc.T("EpicStillTitle"), Loc.T("EpicStillMsg"));
                return;
            }
        }

        g.BeginRemoving(() => Loc.T("RemovingFiles"), indeterminate: false);
        var progress = new Progress<double>(g.SetProgress);
        long expected = g.SizeBytes ?? 0;
        var result = await Task.Run(() => EpicRemover.Remove(info, expected, progress));
        if (result.Success)
        {
            Log.Write($"Epic : {g.Name} supprimé ({info.InstallPath})");
            OnGameRemoved(g);
        }
        else
        {
            g.ResetState();
            ConfirmDialog.Error(this, Loc.T("RemoveFailedTitle"), result.Message);
        }
    }

    private void CheckPendingUninstalls()
    {
        foreach (var g in _games.Where(x => x.IsWaiting).ToList())
        {
            bool still;
            try { still = g.StillInstalled(); }
            catch { still = true; }
            if (!still) OnGameRemoved(g);
            else if (DateTime.Now - g.PendingSince > WaitTimeout) g.ResetState();
        }
    }

    private void OnGameRemoved(GameEntry g)
    {
        _games.Remove(g);
        foreach (var d in _drives) d.RefreshSpace();
        if (!_games.Any(x => x.Platform == g.Platform))
        {
            var chip = _chips.FirstOrDefault(c => c.Platform == g.Platform);
            if (chip != null) _chips.Remove(chip);
            if (chip?.IsChecked == true) RefreshView();
        }
        UpdateStats();
        UpdateEmptyState();
        ShowToast(Loc.F("Uninstalled", g.Name) + (g.SizeBytes is long s && s > 0 ? Loc.F("Freed", Fmt.Size(s)) : ""), success: true);
    }

    // ───────────────────────────── Notification ─────────────────────────────

    private void ShowToast(string text, bool success)
    {
        ToastText.Text = text;
        ToastGlyph.Text = success ? "" : "";
        ToastGlyph.Foreground = (Brush)FindResource(success ? "SuccessBrush" : "AccentBrush");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (_, _) => { if (Toast.Opacity == 0) Toast.Visibility = Visibility.Collapsed; };
        Toast.BeginAnimation(OpacityProperty, anim);
    }

    // ───────────────────────────── Captures (--snapshot) ─────────────────────────────

    private async Task RunSnapshotAsync()
    {
        await Task.WhenAny(Task.WhenAll(_backgroundTasks), Task.Delay(TimeSpan.FromSeconds(150)));
        await Task.Delay(800);
        UpdateStats();
        UpdateLayout();
        try
        {
            if (_options.SnapshotPath != null) SaveVisual(RootGrid, _options.SnapshotPath);
            if (_options.DialogSnapshotPath != null)
            {
                var g = _view?.OfType<GameEntry>().FirstOrDefault();
                if (g != null)
                {
                    var (message, secondary, note) = DescribeUninstall(g);
                    var dlg = ConfirmDialog.Create(this, Loc.F("DlgUninstallTitle", g.Name), message, Details(g), Loc.T("Uninstall"), secondary, note);
                    dlg.ShowActivated = false;
                    dlg.Show();
                    await Task.Delay(500);
                    dlg.UpdateLayout();
                    SaveVisual(dlg, _options.DialogSnapshotPath);
                    dlg.Close();
                }
            }
        }
        catch (Exception ex) { Log.Error("snapshot", ex); }
        Application.Current.Shutdown();
    }

    private void SaveVisual(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
