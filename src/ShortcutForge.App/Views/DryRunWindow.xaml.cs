using System.Windows;
using System.Windows.Controls;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Simulation;

namespace ShortcutForge.App.Views;

/// <summary>Runs the shortcut on Windows step by step and lists what each action produced.</summary>
public partial class DryRunWindow : Window, ISimulationHost
{
    private readonly Shortcut _shortcut;

    public DryRunWindow(Shortcut shortcut)
    {
        InitializeComponent();
        _shortcut = shortcut;
        Title = $"{Title} – {shortcut.Name}";
        Loaded += (_, _) => RunButton.Focus();
    }

    public sealed record StepRow(string Number, string Icon, string KindText, Thickness Indent, string Action, string? Output, string? Note);

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var input = InputBox.Text.Length > 0 ? InputBox.Text : null;
        var result = new ShortcutSimulator(this).Run(_shortcut, input);

        StepList.ItemsSource = result.Steps.Select((s, i) => new StepRow(
            (i + 1).ToString(),
            s.Kind switch { SimStepKind.Ran => "✔", SimStepKind.Simulated => "📱", _ => "⏭" },
            s.Kind switch
            {
                SimStepKind.Ran => L.T("Lefutott", "Ran"),
                SimStepKind.Simulated => L.T("Csak az iPhone-on van hatása – itt csak megjelenik", "Only has an effect on the iPhone – shown here"),
                _ => L.T("Windowson nem futtatható, kimaradt", "Cannot run on Windows, skipped"),
            },
            new Thickness(s.Depth * 18, 0, 0, 0),
            s.Action,
            s.Output,
            s.Note)).ToList();

        var ran = result.Steps.Count(s => s.Kind == SimStepKind.Ran);
        var simulated = result.Steps.Count(s => s.Kind == SimStepKind.Simulated);
        var skipped = result.Steps.Count(s => s.Kind == SimStepKind.Skipped);
        var summary = L.T($"{result.Steps.Count} lépés: {ran} lefutott, {simulated} csak iPhone-on, {skipped} kihagyva.",
            $"{result.Steps.Count} steps: {ran} ran, {simulated} iPhone-only, {skipped} skipped.");
        if (result.StopReason is { } reason) summary += " " + reason;
        if (result.Output is { Length: > 0 } output) summary += L.T($" Kimenet: {output}", $" Output: {output}");
        SummaryText.Text = summary;
    }

    // ---- ISimulationHost: the iPhone's dialogs, shown on Windows -----------------------

    public string? AskText(string prompt, string? defaultAnswer)
    {
        var dialog = new InputDialog(L.T("Kérdés", "Ask for Input"), prompt.Length > 0 ? prompt : L.T("Válasz:", "Answer:"), defaultAnswer ?? "") { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public int? Choose(string prompt, IReadOnlyList<string> items)
    {
        var list = new ListBox { ItemsSource = items, SelectedIndex = 0, MinHeight = 120, MaxHeight = 360 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Style = (Style)FindResource("PrimaryButton") };
        var cancel = new Button { Content = L.T("Mégse", "Cancel"), IsCancel = true, MinWidth = 80, Style = (Style)FindResource("ToolButton") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        if (prompt.Length > 0) panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(list);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Owner = this, Title = L.T("Válassz", "Choose"), Content = panel, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)FindResource("WindowBg"), Foreground = (System.Windows.Media.Brush)FindResource("TextFg"),
        };
        ok.Click += (_, _) => window.DialogResult = list.SelectedIndex >= 0;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) window.DialogResult = true; };
        return window.ShowDialog() == true ? list.SelectedIndex : null;
    }

    public bool Alert(string title, string message, bool cancelShown) =>
        MessageBox.Show(this, message, title.Length > 0 ? title : L.T("Figyelmeztetés", "Alert"),
            cancelShown ? MessageBoxButton.OKCancel : MessageBoxButton.OK, MessageBoxImage.Information) != MessageBoxResult.Cancel;
}
