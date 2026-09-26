using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShortcutForge.App.Editor;
using ShortcutForge.App.ViewModels;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

public partial class MainWindow : Window
{
    private const string CardDragFormat = "ShortcutForge.CardIndex";

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _dslTimer;
    private bool _settingEditorText;
    private bool _dslStale = true;
    private bool _revertingTab;
    private Point? _dragStart;
    private ListBoxItem? _dropHighlight;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new DialogService(this));
        DataContext = _vm;

        DslEditorSupport.Attach(DslEditor, () => _vm.VariableSuggestions);
        DslEditorSupport.EnableFolding(DslEditor);
        _dslTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dslTimer.Tick += (_, _) =>
        {
            _dslTimer.Stop();
            _vm.OnDslEdited(DslEditor.Text);
            UpdateErrorMarker();
        };
        DslEditor.TextChanged += (_, _) =>
        {
            if (_settingEditorText) return;
            _dslTimer.Stop();
            _dslTimer.Start();
        };

        _vm.DslSourceChanged += (_, _) =>
        {
            _dslStale = true;
            if (Tabs.SelectedItem == DslTab && _vm.PendingDsl is null) LoadDslText();
        };

        CommandBindings.Add(new CommandBinding(AppCommands.Help, (_, _) => new HelpWindow { Owner = this }.Show()));
        CommandBindings.Add(new CommandBinding(AppCommands.FocusSearch, (_, _) => FocusSearch()));

        // Keep the scroll position when the cards are recreated; bring new / moved cards into view.
        double savedOffset = 0;
        _vm.CardsRebuilding += (_, _) => savedOffset = CardsScrollViewer()?.VerticalOffset ?? 0;
        _vm.CardsRebuilt += (_, select) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            CardsScrollViewer()?.ScrollToVerticalOffset(savedOffset);
            if (select is { } i && i >= 0 && i < _vm.Cards.Count) CardsList.ScrollIntoView(_vm.Cards[i]);
        });

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && System.IO.File.Exists(args[1])) _vm.OpenPath(args[1]);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushDslTimer();
        if (!_vm.CanClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            L.T($"ShortcutForge {ShortcutForge.Core.AppInfo.Version}\nApple Parancsok (Shortcuts) készítése Windowson.\n\nKészítette: Szilvágyi Krisztián\n{ShortcutForge.Core.AppInfo.Copyright}\n{ShortcutForge.Core.AppInfo.RepositoryUrl}\n\n" +
                $"Akciókatalógus: {ActionCatalog.Default.Actions.Count} beépített akció; bármely más akció (külső appok) " +
                "általános blokként szerkeszthető.\n\nA .shortcut fájlokat iOS 15 óta alá kell írni (Macen: shortcuts sign).",
                $"ShortcutForge {ShortcutForge.Core.AppInfo.Version}\nCreate Apple Shortcuts on Windows.\n\nCreated by Krisztián Szilvágyi\n{ShortcutForge.Core.AppInfo.Copyright}\n{ShortcutForge.Core.AppInfo.RepositoryUrl}\n\n" +
                $"Action catalog: {ActionCatalog.Default.Actions.Count} built-in actions; any other action (third-party apps) " +
                "can be edited as a generic block.\n\nSince iOS 15, .shortcut files must be signed (on a Mac: shortcuts sign)."),
            L.T("Névjegy", "About"), MessageBoxButton.OK, MessageBoxImage.Information);

    // ------------------------------------------------------------------ DSL tab

    private void LoadDslText()
    {
        _settingEditorText = true;
        try
        {
            var caret = DslEditor.CaretOffset;
            DslEditor.Document.Text = _vm.CurrentDsl;
            DslEditor.CaretOffset = Math.Min(caret, DslEditor.Document.TextLength);
        }
        finally
        {
            _settingEditorText = false;
        }
        _vm.DiscardDslEditsSilently();
        _dslStale = false;
        UpdateErrorMarker();
    }

    private void FlushDslTimer()
    {
        if (!_dslTimer.IsEnabled) return;
        _dslTimer.Stop();
        _vm.OnDslEdited(DslEditor.Text);
        UpdateErrorMarker();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || _revertingTab) return;

        if (e.RemovedItems.Contains(DslTab))
        {
            FlushDslTimer();
            if (!_vm.ApplyDslIfNeeded())
            {
                var discard = MessageBox.Show(this,
                    L.T("A szövegben hiba van, így nem alkalmazható:\n\n", "The text has an error and cannot be applied:\n\n") + _vm.DslError +
                    L.T("\n\nElveted a szöveges módosításokat? (Nem = maradok és javítom)", "\n\nDiscard the text changes? (No = stay and fix it)"),
                    L.T("Hibás szöveg", "Invalid text"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (discard == MessageBoxResult.Yes)
                {
                    _vm.DiscardDslEdits();
                }
                else
                {
                    _revertingTab = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        Tabs.SelectedItem = DslTab;
                        _revertingTab = false;
                    });
                    return;
                }
            }
        }

        _vm.IsDslTabActive = Tabs.SelectedItem == DslTab;
        if (_vm.IsDslTabActive && (_dslStale || _vm.PendingDsl is null)) LoadDslText();
    }

    private void UpdateErrorMarker()
    {
        if (_vm.DslError is null || _vm.DslErrorLine <= 0)
        {
            DslEditorSupport.ShowError(DslEditor, -1, 0);
            return;
        }
        var doc = DslEditor.Document;
        var line = Math.Min(_vm.DslErrorLine, doc.LineCount);
        var docLine = doc.GetLineByNumber(line);
        var column = Math.Clamp(_vm.DslErrorColumn, 1, docLine.Length + 1);
        var offset = doc.GetOffset(line, column);
        DslEditorSupport.ShowError(DslEditor, offset, Math.Max(1, _vm.DslErrorLength));
    }

    private void DslError_Click(object sender, MouseButtonEventArgs e)
    {
        if (_vm.DslErrorLine <= 0) return;
        var line = Math.Min(_vm.DslErrorLine, DslEditor.Document.LineCount);
        DslEditor.TextArea.Caret.Line = line;
        DslEditor.TextArea.Caret.Column = _vm.DslErrorColumn;
        DslEditor.ScrollToLine(line);
        DslEditor.Focus();
    }

    // ------------------------------------------------------------------ library keyboard

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void FocusSearch_Click(object sender, RoutedEventArgs e) => FocusSearch();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Add the first match.
                if (_vm.Library.Cast<object>().FirstOrDefault() is ActionDefinition first)
                {
                    _vm.AddAction(first);
                    e.Handled = true;
                }
                break;
            case Key.Down:
                if (_vm.Library.Cast<object>().FirstOrDefault() is { } item)
                {
                    LibraryList.SelectedItem = item;
                    LibraryList.UpdateLayout();
                    (LibraryList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                _vm.SearchText = "";
                e.Handled = true;
                break;
        }
    }

    private void LibraryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LibraryList.SelectedItem is ActionDefinition definition)
        {
            _vm.AddAction(definition);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ library drag & drop

    private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LibraryList.SelectedItem is ActionDefinition definition && IsInsideItem(e.OriginalSource))
            _vm.AddAction(definition);
    }

    private static bool IsInsideItem(object source) =>
        source is DependencyObject d && FindAncestor<ListBoxItem>(d) is not null;

    private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(null);

    private void LibraryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is null || !DragDistanceReached(e)) return;
        if (FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext is not ActionDefinition definition) return;
        _dragStart = null;
        DragDrop.DoDragDrop(LibraryList, new DataObject(typeof(ActionDefinition), definition), DragDropEffects.Copy);
    }

    private bool DragDistanceReached(MouseEventArgs e)
    {
        var diff = e.GetPosition(null) - _dragStart!.Value;
        return Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    // ------------------------------------------------------------------ card drag & drop

    private void CardsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only start dragging from the card header (drag handle), not from input fields or buttons.
        _dragStart = IsOnDragHandle(e.OriginalSource as DependencyObject) ? e.GetPosition(null) : null;
    }

    private static bool IsOnDragHandle(DependencyObject? element)
    {
        for (var d = element; d is not null and not ListBoxItem; d = GetParent(d))
        {
            if (d is TextBox or ButtonBase or ComboBox) return false;
            if (d is FrameworkElement { Tag: "DragHandle" }) return true;
        }
        return false;
    }

    private void CardsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is null || !DragDistanceReached(e)) return;
        if (FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext is not ActionCardViewModel card) return;
        _dragStart = null;
        DragDrop.DoDragDrop(CardsList, new DataObject(CardDragFormat, card.Index), DragDropEffects.Move);
        ClearDropHighlight();
    }

    private (int Index, ListBoxItem? Item, bool Below) DropTarget(DragEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not ActionCardViewModel card) return (_vm.Cards.Count, null, false);
        var below = e.GetPosition(item).Y > item.ActualHeight / 2;
        return (below ? card.Index + 1 : card.Index, item, below);
    }

    private void CardsList_DragOver(object sender, DragEventArgs e)
    {
        var isCard = e.Data.GetDataPresent(CardDragFormat);
        var isAction = e.Data.GetDataPresent(typeof(ActionDefinition));
        e.Effects = isCard ? DragDropEffects.Move : isAction ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        var (_, item, below) = DropTarget(e);
        ClearDropHighlight();
        if (item is null || !(isCard || isAction)) return;
        if (item.Template.FindName("DropLine", item) is Border line)
        {
            line.BorderBrush = (Brush)FindResource("Accent");
            line.BorderThickness = below ? new Thickness(0, 0, 0, 3) : new Thickness(0, 3, 0, 0);
            _dropHighlight = item;
        }
    }

    private void CardsList_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight();

    private void ClearDropHighlight()
    {
        if (_dropHighlight?.Template.FindName("DropLine", _dropHighlight) is Border line)
            line.BorderBrush = Brushes.Transparent;
        _dropHighlight = null;
    }

    private void CardsList_Drop(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        var (index, _, _) = DropTarget(e);
        if (e.Data.GetData(typeof(ActionDefinition)) is ActionDefinition definition)
        {
            _vm.InsertAction(definition, index);
        }
        else if (e.Data.GetData(CardDragFormat) is int from)
        {
            _vm.MoveBlock(from, index);
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ variables (type / property / key, rename)

    private string? PickVariable(string initial)
    {
        var dialog = new VariablePickerWindow(_vm.VariableSuggestions, _vm.Scope, initial) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    /// <summary>"⋯" next to a variable field: edit the variable with its type / property / key.</summary>
    private void VariableDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ComboBox box) return;
        var result = PickVariable(box.Text);
        if (result is null) return;
        box.Text = result;
        box.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
    }

    /// <summary>"{x}" next to a text field: inserts {variable} at the caret.</summary>
    private void InsertVariable_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TextBox box) return;
        var caret = box.CaretIndex;
        var result = PickVariable("");
        if (result is null) return;
        var insert = "{" + result + "}";
        caret = Math.Clamp(caret, 0, box.Text.Length);
        box.Text = box.Text.Insert(caret, insert);
        box.CaretIndex = caret + insert.Length;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.Focus();
    }

    /// <summary>Click on the "→name" badge: rename the action's output (magic variable).</summary>
    private void OutputBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ActionCardViewModel card) return;
        e.Handled = true;
        var current = card.Action.CustomOutputName ?? card.Definition?.Output ?? "";
        var name = new DialogService(this).Prompt(
            L.T("Kimenet átnevezése", "Rename output"),
            L.T("Az akció kimenetének új neve (üresen hagyva visszaáll az alapértelmezettre):",
                "New name for the action's output (leave empty to restore the default):"),
            current);
        if (name is not null) _vm.RenameOutput(card, name);
    }

    // ------------------------------------------------------------------ helpers

    private ScrollViewer? CardsScrollViewer() => FindDescendant<ScrollViewer>(CardsList);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = GetParent(d);
        return d as T;
    }
}
