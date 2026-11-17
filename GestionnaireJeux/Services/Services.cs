using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GestionnaireJeux.Scanning;

namespace GestionnaireJeux.Services;

public static class FolderSize
{
    /// <summary>Taille totale des fichiers d'un dossier (sans suivre les jonctions). Null si inaccessible.</summary>
    public static long? TryMeasure(string path, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(path) || Paths.IsProtected(path, out _)) return null;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // compte aussi les fichiers cachés/système
                ReturnSpecialDirectories = false,
            };
            var files = new FileSystemEnumerable<long>(path, (ref FileSystemEntry e) => e.Length, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,
            };
            long total = 0;
            int n = 0;
            foreach (var len in files)
            {
                total += len;
                if ((++n & 2047) == 0) ct.ThrowIfCancellationRequested();
            }
            return total;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error("taille " + path, ex);
            return null;
        }
    }
}

public static class IconLoader
{
    public static ImageSource? Load(IEnumerable<string> sources, int size = 64)
    {
        foreach (var s in sources)
        {
            var img = TryLoad(s, size);
            if (img != null) return img;
        }
        return null;
    }

    private static ImageSource? TryLoad(string source, int size)
    {
        try
        {
            var (path, index) = IconRef.Parse(source);
            if (path == null || !File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = size;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }

            // .ico / .exe / .dll : extraction à la bonne taille via l'API Windows
            var handles = new IntPtr[1];
            var ids = new uint[1];
            uint count = Native.PrivateExtractIcons(path, index, size, size, handles, ids, 1, 0);
            if (count == 0 || count == uint.MaxValue || handles[0] == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var copy = new WriteableBitmap(src);
                copy.Freeze();
                return copy;
            }
            finally
            {
                Native.DestroyIcon(handles[0]);
            }
        }
        catch
        {
            return null;
        }
    }
}

public static class CommandLine
{
    /// <summary>Sépare une UninstallString en (exécutable, arguments), y compris les chemins non entre guillemets.</summary>
    public static (string File, string Args) Split(string command)
    {
        var s = Environment.ExpandEnvironmentVariables(command.Trim());
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end > 0 ? (s[1..end], s[(end + 1)..].Trim()) : (s.Trim('"'), "");
        }

        int pos = 0;
        while (true)
        {
            int sp = s.IndexOf(' ', pos);
            var candidate = sp < 0 ? s : s[..sp];
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                return (candidate, sp < 0 ? "" : s[(sp + 1)..].Trim());
            if (sp < 0) break;
            pos = sp + 1;
        }

        var m = Regex.Match(s, @"^(.+?\.exe)(?=\s|$)(.*)$", RegexOptions.IgnoreCase);
        if (m.Success) return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
        int first = s.IndexOf(' ');
        return first < 0 ? (s, "") : (s[..first], s[(first + 1)..].Trim());
    }
}

public static class Uninstaller
{
    public static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    /// <summary>Lance le désinstalleur officiel (URI du launcher ou programme de désinstallation).</summary>
    public static void LaunchExternal(GameEntry g)
    {
        if (g.Kind == UninstallKind.Uri && g.UninstallUri != null)
        {
            OpenUri(g.UninstallUri);
            return;
        }
        if (g.UninstallCommand == null) throw new InvalidOperationException(Loc.T("NoUninstallCommand"));

        var (file, args) = CommandLine.Split(g.UninstallCommand);
        var exe = Path.GetFileName(file);
        if (exe.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            args = Regex.Replace(args, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase); // /I = « modifier », /X = désinstaller

        var psi = new ProcessStartInfo(file, args) { UseShellExecute = true }; // ShellExecute : permet l'invite UAC
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
        }
        catch { }
        Process.Start(psi);
    }

    /// <summary>Désinstalle un paquet Xbox/Store via Remove-AppxPackage (comme l'app Xbox ou les Paramètres).</summary>
    public static async Task<(bool Ok, string Error)> RemoveAppxAsync(string fullName)
    {
        if (!Regex.IsMatch(fullName, @"^[A-Za-z0-9._~\-]+$")) return (false, Loc.T("InvalidPackage"));
        var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " +
                     $"Remove-AppxPackage -Package '{fullName}' -ErrorAction Stop";
        var psi = new ProcessStartInfo(ps)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var errTask = p.StandardError.ReadToEndAsync();
        var outTask = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        var err = (await errTask).Trim();
        await outTask;
        return (p.ExitCode == 0, CleanPowerShellError(err));
    }

    private static string CleanPowerShellError(string err)
    {
        if (string.IsNullOrWhiteSpace(err)) return "";
        var lines = err.Split('\n').Select(l => l.Trim())
            .TakeWhile(l => !l.StartsWith("Au caractère", StringComparison.OrdinalIgnoreCase)
                            && !l.StartsWith("At line", StringComparison.OrdinalIgnoreCase)
                            && !l.StartsWith("+ ", StringComparison.Ordinal))
            .Where(l => l.Length > 0);
        var text = string.Join(" ", lines);
        return text.Length > 500 ? text[..500] + "…" : text;
    }
}

public sealed class EpicRemovalInfo
{
    public required string InstallPath { get; init; }
    public required List<string> ManifestPaths { get; init; }
    public required List<string> AppNames { get; init; }
    public string? LauncherInstalledPath { get; init; }
}

public sealed record RemovalResult(bool Success, string Message)
{
    public static RemovalResult Ok() => new(true, "");
    public static RemovalResult Fail(string message) => new(false, message);
}

/// <summary>
/// Désinstallation directe d'un jeu Epic : suppression des fichiers puis retrait du jeu des manifestes
/// d'Epic Games Launcher (ce que fait le launcher lui-même). Les métadonnées sont sauvegardées avant.
/// </summary>
public static class EpicRemover
{
    public const string LibraryUri = "com.epicgames.launcher://store/library";

    public static bool IsLauncherRunning() => Process.GetProcessesByName("EpicGamesLauncher").Length > 0;

    public static async Task<bool> CloseLauncherAsync()
    {
        foreach (var p in Process.GetProcessesByName("EpicGamesLauncher"))
        {
            // Pas entireProcessTree : un jeu lancé depuis Epic est un enfant du launcher et ne doit pas être tué.
            try { p.Kill(); } catch { }
            finally { p.Dispose(); }
        }
        for (int i = 0; i < 40 && IsLauncherRunning(); i++) await Task.Delay(250);
        return !IsLauncherRunning();
    }

    public static string? CheckSafe(string installPath)
    {
        if (Paths.IsProtected(installPath, out var reason)) return reason;
        var leaf = Path.GetFileName(installPath.TrimEnd('\\'));
        foreach (var lib in new[] { "Epic Games", "SteamLibrary", "steamapps", "common", "XboxGames", "Games", "Jeux" })
            if (leaf.Equals(lib, StringComparison.OrdinalIgnoreCase)) return Loc.T("ReasonLibrary");
        return null;
    }

    public static RemovalResult Remove(EpicRemovalInfo info, long expectedBytes, IProgress<double>? progress)
    {
        var path = info.InstallPath;
        if (Directory.Exists(path))
        {
            var unsafeReason = CheckSafe(path);
            if (unsafeReason != null) return RemovalResult.Fail(Loc.F("SafetyRefused", unsafeReason));
        }

        BackupMetadata(info);

        if (Directory.Exists(path))
        {
            int failures = DeleteTree(path, expectedBytes, progress);
            if (Directory.Exists(path))
            {
                Log.Write($"Epic : {failures} élément(s) non supprimé(s) dans {path}, tentative avec droits administrateur");
                if (!DeleteElevated(path))
                    return RemovalResult.Fail(Loc.T("EpicPartial"));
            }
        }

        try
        {
            foreach (var m in info.ManifestPaths)
                if (File.Exists(m)) File.Delete(m);
            if (info.LauncherInstalledPath != null && File.Exists(info.LauncherInstalledPath))
                RemoveFromLauncherInstalled(info.LauncherInstalledPath, info.AppNames);
        }
        catch (Exception ex)
        {
            Log.Error("epic manifestes", ex);
            return RemovalResult.Fail(Loc.F("EpicMetaFailed", ex.Message));
        }

        progress?.Report(1);
        return RemovalResult.Ok();
    }

    private static void BackupMetadata(EpicRemovalInfo info)
    {
        try
        {
            var dir = Path.Combine(Log.Folder, "Sauvegardes", "Epic", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            foreach (var m in info.ManifestPaths)
                if (File.Exists(m)) File.Copy(m, Path.Combine(dir, Path.GetFileName(m)), overwrite: true);
            if (info.LauncherInstalledPath != null && File.Exists(info.LauncherInstalledPath))
                File.Copy(info.LauncherInstalledPath, Path.Combine(dir, "LauncherInstalled.dat"), overwrite: true);
            File.WriteAllText(Path.Combine(dir, "jeu.txt"), info.InstallPath);
        }
        catch (Exception ex) { Log.Error("sauvegarde epic", ex); }
    }

    private static void RemoveFromLauncherInstalled(string datPath, IReadOnlyCollection<string> appNames)
    {
        var root = JsonNode.Parse(File.ReadAllText(datPath));
        if (root?["InstallationList"] is not JsonArray list) return;
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var app = list[i]?["AppName"]?.GetValue<string>();
            if (app != null && appNames.Contains(app, StringComparer.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) File.WriteAllText(datPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Supprime une arborescence sans jamais suivre les jonctions/liens (le lien est supprimé, pas sa cible).</summary>
    public static int DeleteTree(string root, long expectedBytes, IProgress<double>? progress)
    {
        long done = 0;
        int failures = 0;
        var sw = Stopwatch.StartNew();
        var listOptions = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

        void Report()
        {
            if (progress == null || expectedBytes <= 0 || sw.ElapsedMilliseconds < 120) return;
            sw.Restart();
            progress.Report(Math.Min(0.99, (double)done / expectedBytes));
        }

        void Recurse(DirectoryInfo dir)
        {
            List<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos("*", listOptions).ToList(); }
            catch { failures++; return; }

            foreach (var entry in entries)
            {
                bool isLink = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (entry is DirectoryInfo sub && !isLink)
                {
                    Recurse(sub);
                    continue;
                }
                try
                {
                    long len = entry is FileInfo fi && !isLink ? fi.Length : 0;
                    if (entry.Attributes.HasFlag(FileAttributes.ReadOnly)) entry.Attributes &= ~FileAttributes.ReadOnly;
                    entry.Delete();
                    done += len;
                    Report();
                }
                catch { failures++; }
            }
            try
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReadOnly)) dir.Attributes &= ~FileAttributes.ReadOnly;
                dir.Delete(false);
            }
            catch { failures++; }
        }

        Recurse(new DirectoryInfo(root));
        return failures;
    }

    private static bool DeleteElevated(string path)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c rd /s /q \"{path}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return !Directory.Exists(path);
        }
        catch (Win32Exception) { return false; } // UAC refusé
    }
}

/// <summary>Export JSON de l'analyse (option --scan-json), utile pour vérifier ce qui est détecté.</summary>
public static class ScanExport
{
    public static void Run(string outPath)
    {
        var sw = Stopwatch.StartNew();
        var games = GameScanner.ScanAll();
        var scanMs = sw.ElapsedMilliseconds;
        var rows = games.Select(g =>
        {
            long? size = g.SizeBytes;
            bool approx = g.SizeIsApprox;
            if (g.NeedsMeasure && g.MeasurePath != null)
            {
                var m = FolderSize.TryMeasure(g.MeasurePath, CancellationToken.None);
                if (m != null) { size = m; approx = false; }
            }
            return new
            {
                g.Name,
                Platform = g.PlatformLabel,
                Drive = g.DriveLabel,
                g.InstallPath,
                Size = size is long s ? Fmt.Size(s) : null,
                Approx = approx,
                Kind = g.Kind.ToString(),
                Uninstall = g.UninstallUri ?? g.UninstallCommand ?? g.PackageFullName ?? g.Epic?.InstallPath,
                LastPlayed = g.LastPlayed?.ToString("yyyy-MM-dd"),
                Icon = IconLoader.Load(g.IconSources) != null,
                IconSource = g.IconSources.FirstOrDefault(),
                StillInstalled = g.StillInstalled(),
                g.Note,
            };
        }).ToList();
        var json = JsonSerializer.Serialize(
            new { ScanMs = scanMs, TotalMs = sw.ElapsedMilliseconds, Count = rows.Count, Games = rows },
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(outPath, json);
    }
}
