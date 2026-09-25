using System.Windows;
using Microsoft.Win32;
using ShortcutForge.App.Services;
using ShortcutForge.Signing;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        UnsignedRadio.IsChecked = settings.Signer == SignerKind.Unsigned;
        MacRadio.IsChecked = settings.Signer == SignerKind.MacSsh;
        HostBox.Text = settings.MacHost;
        PortBox.Text = settings.MacPort.ToString();
        UserBox.Text = settings.MacUser;
        PasswordBox.Password = settings.MacPassword ?? "";
        KeyBox.Text = settings.MacKeyPath ?? "";
        ModeBox.SelectedIndex = settings.SigningMode == SigningMode.Anyone ? 0 : 1;
    }

    private MacSshSettings CurrentSsh() => new()
    {
        Host = HostBox.Text.Trim(),
        Port = int.TryParse(PortBox.Text, out var port) ? port : 22,
        User = UserBox.Text.Trim(),
        Password = PasswordBox.Password,
        PrivateKeyPath = string.IsNullOrWhiteSpace(KeyBox.Text) ? null : KeyBox.Text.Trim(),
    };

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = L.T("Minden fájl|*.*", "All files|*.*"), Title = L.T("SSH privát kulcs (OpenSSH formátum)", "SSH private key (OpenSSH format)") };
        if (dialog.ShowDialog(this) == true) KeyBox.Text = dialog.FileName;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestResult.Foreground = (System.Windows.Media.Brush)FindResource("MutedText");
        TestResult.Text = L.T("Kapcsolódás…", "Connecting…");
        try
        {
            TestResult.Text = await new MacSshSigner(CurrentSsh()).TestConnectionAsync();
            TestResult.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex) when (ex is SigningException or Renci.SshNet.Common.SshException or ArgumentException
                                       or System.IO.IOException or InvalidOperationException)
        {
            TestResult.Text = ex.Message;
            TestResult.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var ssh = CurrentSsh();
        _settings.Signer = MacRadio.IsChecked == true ? SignerKind.MacSsh : SignerKind.Unsigned;
        _settings.MacHost = ssh.Host;
        _settings.MacPort = ssh.Port;
        _settings.MacUser = ssh.User;
        _settings.MacPassword = ssh.Password;
        _settings.MacKeyPath = ssh.PrivateKeyPath;
        _settings.SigningMode = ModeBox.SelectedIndex == 1 ? SigningMode.PeopleWhoKnowMe : SigningMode.Anyone;
        DialogResult = true;
    }
}
