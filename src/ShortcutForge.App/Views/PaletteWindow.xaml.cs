using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShortcutForge.App.ViewModels;

namespace ShortcutForge.App.Views;

/// <summary>Command palette (Ctrl+K): type to find any command, template or action, Enter to run it.</summary>
public partial class PaletteWindow : Window
{
    private readonly IReadOnlyList<PaletteItem> _items;
    private bool _closing;

    public PaletteWindow(Window owner, IReadOnlyList<PaletteItem> items)
    {
        InitializeComponent();
        Owner = owner;
        _items = items;

        // Near the top, centered over the main window (like in code editors).
        Left = owner.Left + (owner.ActualWidth - Width) / 2;
        Top = owner.Top + 90;

        Filter();
        Loaded += (_, _) => QueryBox.Focus();
    }

    private void Filter()
    {
        var results = PaletteMatcher.Filter(_items, QueryBox.Text);
        ResultList.ItemsSource = results;
        if (results.Count > 0) ResultList.SelectedIndex = 0;
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Filter();

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = ResultList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex + 1) % count;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex - 1 + count) % count;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseOnce();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RunSelected();

    private void RunSelected()
    {
        if (ResultList.SelectedItem is not PaletteItem item) return;
        CloseOnce();
        // Run after the palette is gone, so dialogs open over the main window.
        Owner?.Dispatcher.BeginInvoke(item.Execute);
    }

    private void Window_Deactivated(object? sender, EventArgs e) => CloseOnce();

    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
