using System.Windows;
using System.Windows.Threading;
using GestionnaireJeux.Services;

namespace GestionnaireJeux;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var args = e.Args;
        string? Arg(string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // Langue et thème : préférences enregistrées, sinon ceux de Windows
        var settings = AppSettings.Load();
        Loc.Instance.SetLanguage(Arg("--lang") ?? settings.Language);
        var theme = Arg("--theme") ?? settings.Theme;
        ThemeManager.Apply(theme == null ? ThemeManager.SystemPrefersDark() : theme.Equals("dark", StringComparison.OrdinalIgnoreCase));

        // Export de l'analyse sans interface : GestionnaireJeux.exe --scan-json resultat.json
        if (Arg("--scan-json") is string jsonPath)
        {
            try { ScanExport.Run(jsonPath); }
            catch (Exception ex) { Log.Error("scan-json", ex); }
            Shutdown();
            return;
        }

        var window = new MainWindow(new StartupOptions(
            SnapshotPath: Arg("--snapshot"),
            DialogSnapshotPath: Arg("--snapshot-dialog"),
            Drive: Arg("--drive"),
            Sort: Arg("--sort"),
            Search: Arg("--search")));
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("non gérée", e.Exception);
        e.Handled = true;
        try
        {
            ConfirmDialog.Error(MainWindow, Loc.T("UnexpectedError"),
                e.Exception.Message + "\n\n" + Loc.F("LogDetails", Log.FilePath));
        }
        catch { /* rien de plus à faire */ }
    }
}
