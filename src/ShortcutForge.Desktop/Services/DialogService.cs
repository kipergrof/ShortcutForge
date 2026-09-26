using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ShortcutForge.Core.Localization;
using ShortcutForge.Desktop.ViewModels;
using ShortcutForge.Editor;

namespace ShortcutForge.Desktop.Services;

/// <summary>File pickers (Avalonia StorageProvider) and simple message boxes owned by the main window.</summary>
public sealed class DialogService(Window owner) : IDesktopDialogs
{
    public async Task<string?> OpenFileAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Megnyitás", "Open"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L.T("Minden támogatott", "All supported"))
                {
                    Patterns = ShortcutDocument.OpenExtensions.Select(e => "*" + e).ToList(),
                },
                new FilePickerFileType(L.T("ShortcutForge szöveg (*.sfdsl)", "ShortcutForge text (*.sfdsl)")) { Patterns = ["*.sfdsl"] },
                new FilePickerFileType(L.T("Shortcut fájl (*.shortcut)", "Shortcut file (*.shortcut)")) { Patterns = ["*.shortcut"] },
                new FilePickerFileType("Plist (*.plist, *.wflow)") { Patterns = ["*.plist", "*.wflow"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count == 0 ? null : LocalPath(files[0]);
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string typeName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType($"{typeName} (*{extension})") { Patterns = ["*" + extension] }],
        });
        if (file is null) return null;
        var path = LocalPath(file);
        // Some platform pickers do not append the extension.
        if (path is not null && !Path.HasExtension(path)) path += extension;
        return path;
    }

    private string? LocalPath(IStorageItem item)
    {
        var path = item.TryGetLocalPath();
        if (path is null)
            _ = ErrorAsync(L.T("Fájl", "File"), L.T("Csak helyi fájlok támogatottak.", "Only local files are supported."));
        return path;
    }

    public async Task<bool> ConfirmAsync(string title, string message) =>
        await ShowAsync(title, message, [(L.T("Igen", "Yes"), 1, true), (L.T("Nem", "No"), 0, false)]) == 1;

    public async Task<bool?> AskYesNoCancelAsync(string title, string message) =>
        await ShowAsync(title, message, [(L.T("Igen", "Yes"), 1, true), (L.T("Nem", "No"), 0, false), (L.T("Mégse", "Cancel"), -1, false)]) switch
        {
            1 => true,
            0 => false,
            _ => null,
        };

    public Task InfoAsync(string title, string message) => ShowAsync(title, message, [("OK", 1, true)]);

    public Task ErrorAsync(string title, string message) => ShowAsync("⚠ " + title, message, [("OK", 1, true)]);

    /// <summary>A small modal message box; returns the result of the clicked button (-1 when closed).</summary>
    private Task<int> ShowAsync(string title, string message, (string Text, int Result, bool IsDefault)[] buttons)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var result = -1;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, value, isDefault) in buttons)
        {
            var button = new Button { Content = text, MinWidth = 80, IsDefault = isDefault, IsCancel = buttons.Length > 1 && value == buttons[^1].Result,HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault) button.Classes.Add("accent");
            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            row.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 500 },
                row,
            },
        };
        var tcs = new TaskCompletionSource<int>();
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        _ = dialog.ShowDialog(owner);
        return tcs.Task;
    }
}
