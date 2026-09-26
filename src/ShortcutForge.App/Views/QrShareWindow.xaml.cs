using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using ShortcutForge.App.Services;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

/// <summary>Serves a shortcut on the local network and shows its URL as a QR code for the iPhone camera.</summary>
public partial class QrShareWindow : Window
{
    private readonly LocalShareServer _server;

    public QrShareWindow(byte[] data, string fileName, bool isSigned)
    {
        InitializeComponent();
        UnsignedWarning.Visibility = isSigned ? Visibility.Collapsed : Visibility.Visible;

        _server = LocalShareServer.Start(data, fileName);
        _server.PageOpened += (_, endpoint) => Dispatcher.BeginInvoke(() =>
        {
            // The phone got through, so whatever the firewall check guessed is moot.
            FirewallWarning.Visibility = Visibility.Collapsed;
            if (_server.Downloads > 0) return; // already past this step
            StatusText.Text = L.T($"✓ A telefon csatlakozott ({endpoint}) – koppints a Letöltés gombra.",
                $"✓ The phone connected ({endpoint}) – tap the Download button.");
        });
        _server.Downloaded += (_, endpoint) => Dispatcher.BeginInvoke(() =>
            StatusText.Text = L.T($"✓ Letöltve ({_server.Downloads}×) – {endpoint}", $"✓ Downloaded ({_server.Downloads}×) – {endpoint}"));

        // Registered before the early return below, so the listener never outlives the window
        // and keeps PreferredPort taken.
        Closed += (_, _) => _server.Dispose();

        var addresses = LocalShareServer.GetLanAddresses();
        if (addresses.Count == 0)
        {
            StatusText.Text = L.T("Nem található helyi hálózati kapcsolat. Csatlakozz egy Wi-Fi vagy vezetékes hálózathoz.",
                "No local network connection found. Connect to a Wi-Fi or wired network.");
            AddressPanel.Visibility = Visibility.Collapsed;
            return;
        }
        AddressBox.ItemsSource = addresses;
        AddressPanel.Visibility = addresses.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        AddressBox.SelectedIndex = 0;
        StatusText.Text = L.T("Várakozás az iPhone-ra…", "Waiting for the iPhone…");
        _ = CheckFirewallAsync();
    }

    /// <summary>
    /// Warns when Windows would block the phone, which otherwise looks like the QR code simply not
    /// working. Runs off the UI thread: the check walks every firewall rule over COM.
    /// </summary>
    private async Task CheckFirewallAsync()
    {
        var port = _server.Port;
        // null = undeterminable; only a definite "blocked" is worth alarming about.
        if (await RunStaAsync(() => FirewallHelper.IsInboundAllowed(port)) != false) return;

        FirewallWarningText.Text = L.T(
            $"A Windows tűzfal blokkolja a bejövő kapcsolatokat a(z) {port}-es porton, ezért az iPhone nem éri el ezt a gépet. Az Engedélyezés egy szűk szabályt vesz fel (csak ez a port, csak a helyi hálózatról), és rendszergazdai jóváhagyást kér.",
            $"Windows Firewall is blocking incoming connections on port {port}, so the iPhone cannot reach this PC. Allow adds a narrow rule (this port only, local subnet only) and asks for administrator approval.");
        FirewallWarning.Visibility = Visibility.Visible;
    }

    private async void AllowFirewall_Click(object sender, RoutedEventArgs e)
    {
        var port = _server.Port;
        FirewallAllowButton.IsEnabled = false;
        var added = await RunStaAsync(() => FirewallHelper.AllowPort(port));
        if (added && await RunStaAsync(() => FirewallHelper.IsInboundAllowed(port)) != false)
        {
            FirewallWarning.Visibility = Visibility.Collapsed;
            return;
        }
        FirewallAllowButton.IsEnabled = true;
        FirewallWarningText.Text = L.T(
            "A szabályt nem sikerült felvenni – elutasítottad a rendszergazdai kérdést? Felveheted kézzel is, vagy tedd mindkét eszközt ugyanarra a magánhálózatra.",
            "The rule could not be added – was the administrator prompt declined? You can add it manually, or put both devices on the same private network.");
    }

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread but in a single-threaded apartment, which the
    /// firewall work needs: ShellExecuteEx (the UAC prompt) and the COM firewall policy object can
    /// both end up in shell extensions that require an STA, and the thread pool is MTA.
    /// </summary>
    private static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void ShowUrl(string url)
    {
        UrlBox.Text = url;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        QrImage.Source = image;
    }

    private void AddressBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // The QR code points at the landing page, not straight at the file: Safari shows a blank
        // page and offers no download when a scanned link answers with an octet-stream attachment.
        if (AddressBox.SelectedItem is LocalShareServer.LocalAddress address) ShowUrl(_server.PageUrlFor(address.Address));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (UrlBox.Text.Length > 0) Clipboard.SetText(UrlBox.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
