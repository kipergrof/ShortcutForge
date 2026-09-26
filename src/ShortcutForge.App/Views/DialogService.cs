using System.Windows;
using Microsoft.Win32;
using ShortcutForge.App.Services;
using ShortcutForge.App.ViewModels;

namespace ShortcutForge.App.Views;

public sealed class DialogService(Window owner) : IDialogService
{
    public string? OpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? Prompt(string title, string message, string initial = "")
    {
        var dialog = new InputDialog(title, message, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? AskYesNoCancel(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public void Info(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public string? GenerateWithAi(string? apiKey)
    {
        var window = new AiGenerateWindow(apiKey) { Owner = owner };
        return window.ShowDialog() == true ? window.ResultCode : null;
    }

    public bool EditSettings(AppSettings settings) =>
        new SettingsWindow(settings) { Owner = owner }.ShowDialog() == true;
}
