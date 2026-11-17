using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GestionnaireJeux;

public enum DialogChoice { Cancel, Primary, Secondary }

public enum DialogKind { Danger, Warning, Info, Error }

public partial class ConfirmDialog : Window
{
    public DialogChoice Choice { get; private set; } = DialogChoice.Cancel;

    public ConfirmDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => (NoButton.Visibility == Visibility.Visible ? NoButton : YesButton).Focus();
    }

    public static ConfirmDialog Create(Window? owner, string title, string message,
        IEnumerable<(string Label, string Value)>? details, string primaryText,
        string? secondaryText = null, string? note = null, DialogKind kind = DialogKind.Danger, bool showCancel = true)
    {
        var d = new ConfirmDialog();
        if (owner != null && owner.IsLoaded) d.Owner = owner;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        d.YesButton.Content = primaryText;
        d.NoButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        d.AltButton.Content = secondaryText;
        d.AltButton.Visibility = secondaryText == null ? Visibility.Collapsed : Visibility.Visible;
        d.NoteText.Text = note ?? "";
        d.NoteText.Visibility = string.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;

        var (glyph, fg, bg, style) = kind switch
        {
            DialogKind.Warning => ("", "WarningBrush", "WarningSoftBrush", "PrimaryButton"),
            DialogKind.Info => ("", "AccentBrush", "AccentSoftBrush", "PrimaryButton"),
            DialogKind.Error => ("", "DangerBrush", "DangerSoftBrush", "PrimaryButton"),
            _ => ("", "DangerBrush", "DangerSoftBrush", "DangerSolidButton"),
        };
        d.GlyphText.Text = glyph;
        d.GlyphText.Foreground = (Brush)d.FindResource(fg);
        d.GlyphBadge.Background = (Brush)d.FindResource(bg);
        d.YesButton.Style = (Style)d.FindResource(style);

        var rows = details?.ToList() ?? new List<(string, string)>();
        d.DetailsBox.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (label, value) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = label, Foreground = (Brush)d.FindResource("TextFaintBrush") });
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            d.DetailsPanel.Children.Add(grid);
        }
        return d;
    }

    public static DialogChoice Ask(Window? owner, string title, string message,
        IEnumerable<(string Label, string Value)>? details, string primaryText,
        string? secondaryText = null, string? note = null, DialogKind kind = DialogKind.Danger)
    {
        var d = Create(owner, title, message, details, primaryText, secondaryText, note, kind);
        d.ShowDialog();
        return d.Choice;
    }

    public static void Error(Window? owner, string title, string message)
    {
        var d = Create(owner, title, message, null, Loc.T("OK"), kind: DialogKind.Error, showCancel: false);
        d.ShowDialog();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { Choice = DialogChoice.Primary; Close(); }

    private void No_Click(object sender, RoutedEventArgs e) { Choice = DialogChoice.Cancel; Close(); }

    private void Alt_Click(object sender, RoutedEventArgs e) { Choice = DialogChoice.Secondary; Close(); }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
