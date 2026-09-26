using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using ShortcutForge.Core;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Desktop.Services;
using ShortcutForge.Desktop.ViewModels;

namespace ShortcutForge.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly DialogService _dialogs;
    private DispatcherTimer _dslTimer = null!;
    private bool _settingEditorText;
    private bool _dslStale = true;
    private bool _revertingTab;
    private bool _closeConfirmed;

    /// <summary>For the XAML previewer.</summary>
    public MainWindow() : this(new DesktopSettings(), [])
    {
    }

    public MainWindow(DesktopSettings settings, string[] args)
    {
        InitializeComponent();
        _dialogs = new DialogService(this);
        _vm = new MainWindowViewModel(_dialogs, settings);
        DataContext = _vm;

        SetUpDslEditor();
        SetUpKeyboard();
        SetUpRecentFiles();

        Tabs.SelectionChanged += Tabs_SelectionChanged;
        DslErrorPanel.PointerPressed += (_, _) => JumpToDslError();
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _vm.AddFirstMatchCommand.Execute(null);
            e.Handled = true;
        };
        _vm.CardsRebuilt += (_, select) => Dispatcher.UIThread.Post(() =>
        {
            if (select is { } i && i >= 0 && i < _vm.Cards.Count) CardsList.ScrollIntoView(i);
        }, DispatcherPriority.Loaded);

        Opened += async (_, _) =>
        {
            var file = args.FirstOrDefault(File.Exists);
            if (file is not null) await _vm.OpenPathAsync(file);
        };
    }

    // ------------------------------------------------------------------ text (DSL) view

    private void SetUpDslEditor()
    {
        DslEditor.Options.ConvertTabsToSpaces = true;
        DslEditor.Options.IndentationSize = 4;
        DslEditor.Options.EnableHyperlinks = false;
        DslEditor.Options.EnableEmailHyperlinks = false;
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();

        _dslTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dslTimer.Tick += (_, _) =>
        {
            _dslTimer.Stop();
            _vm.OnDslEdited(DslEditor.Text);
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
            if (IsDslTabSelected && _vm.PendingDsl is null) LoadDslText();
        };
    }

    private void ApplyEditorTheme() =>
        DslEditor.SyntaxHighlighting = DslHighlighting.Get(ActualThemeVariant == ThemeVariant.Dark);

    private bool IsDslTabSelected => ReferenceEquals(Tabs.SelectedItem, DslTab);

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
    }

    /// <summary>Validates edits still waiting for the timer.</summary>
    private void FlushDslTimer()
    {
        if (!_dslTimer.IsEnabled) return;
        _dslTimer.Stop();
        _vm.OnDslEdited(DslEditor.Text);
    }

    private async void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Tabs) || _revertingTab) return;

        if (e.RemovedItems.Contains(DslTab))
        {
            FlushDslTimer();
            if (!_vm.ApplyDslIfNeeded())
            {
                // Stay on the text tab while asking.
                _revertingTab = true;
                Tabs.SelectedItem = DslTab;
                _revertingTab = false;
                var discard = await _dialogs.ConfirmAsync(L.T("Hibás szöveg", "Invalid text"),
                    L.T("A szövegben hiba van, így nem alkalmazható:\n\n", "The text has an error and cannot be applied:\n\n") + _vm.DslError +
                    L.T("\n\nElveted a szöveges módosításokat? (Nem = maradok és javítom)", "\n\nDiscard the text changes? (No = stay and fix it)"));
                if (!discard) return;
                _vm.DiscardDslEdits();
                Tabs.SelectedItem = VisualTab;
                return;
            }
        }

        if (IsDslTabSelected && (_dslStale || _vm.PendingDsl is null)) LoadDslText();
    }

    private void JumpToDslError()
    {
        if (_vm.DslErrorLine <= 0 || DslEditor.Document.LineCount == 0) return;
        var line = Math.Min(_vm.DslErrorLine, DslEditor.Document.LineCount);
        var docLine = DslEditor.Document.GetLineByNumber(line);
        var column = Math.Clamp(_vm.DslErrorColumn, 1, docLine.Length + 1);
        DslEditor.TextArea.Caret.Position = new AvaloniaEdit.TextViewPosition(line, column);
        DslEditor.ScrollToLine(line);
        DslEditor.Focus();
    }

    // ------------------------------------------------------------------ keyboard and menus

    private void SetUpKeyboard()
    {
        // Ctrl on Windows / Linux, Cmd on macOS.
        var cmd = PlatformSettings?.HotkeyConfiguration.CommandModifiers
                  ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        void Bind(MenuItem? item, Key key, KeyModifiers modifiers, System.Windows.Input.ICommand command)
        {
            var gesture = new KeyGesture(key, modifiers);
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
            if (item is not null) item.InputGesture = gesture;
        }

        Bind(NewMenuItem, Key.N, cmd, _vm.NewCommand);
        Bind(OpenMenuItem, Key.O, cmd, _vm.OpenCommand);
        Bind(SaveMenuItem, Key.S, cmd, _vm.SaveFileCommand);
        Bind(SaveAsMenuItem, Key.S, cmd | KeyModifiers.Shift, _vm.SaveFileAsCommand);
        Bind(ExportShortcutyMenuItem, Key.E, cmd, _vm.ExportShortcutyCommand);
        Bind(UndoMenuItem, Key.Z, cmd, _vm.UndoCommand);
        Bind(RedoMenuItem, OperatingSystem.IsMacOS() ? Key.Z : Key.Y, OperatingSystem.IsMacOS() ? cmd | KeyModifiers.Shift : cmd, _vm.RedoCommand);
        Bind(DuplicateMenuItem, Key.D, cmd, _vm.DuplicateCardCommand);
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, cmd),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); }),
        });
        if (!OperatingSystem.IsMacOS()) ExitMenuItem.InputGesture = new KeyGesture(Key.F4, KeyModifiers.Alt);
    }

    private void SetUpRecentFiles()
    {
        void Rebuild()
        {
            RecentMenu.Items.Clear();
            foreach (var path in _vm.RecentFiles)
                RecentMenu.Items.Add(new MenuItem { Header = path, Command = _vm.OpenRecentCommand, CommandParameter = path });
        }

        _vm.RecentFiles.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await _dialogs.InfoAsync(L.T("Névjegy", "About"),
            L.T($"ShortcutForge {AppInfo.Version} (macOS / Linux előzetes)\nApple Parancsok (Shortcuts) készítése.\n\n{AppInfo.Copyright}\n{AppInfo.RepositoryUrl}\n\n" +
                $"Akciókatalógus: {ActionCatalog.Default.Actions.Count} beépített akció.",
                $"ShortcutForge {AppInfo.Version} (macOS / Linux preview)\nCreate Apple Shortcuts.\n\n{AppInfo.Copyright}\n{AppInfo.RepositoryUrl}\n\n" +
                $"Action catalog: {ActionCatalog.Default.Actions.Count} built-in actions."));

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed) return;
        FlushDslTimer();
        if (!_vm.IsDirty && _vm.PendingDsl is null) return;
        // Dialogs are asynchronous: cancel now and close again after the user answered.
        e.Cancel = true;
        if (!await _vm.CanCloseAsync()) return;
        _closeConfirmed = true;
        Close();
    }
}
