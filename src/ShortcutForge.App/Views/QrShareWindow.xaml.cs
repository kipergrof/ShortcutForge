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
        _server.Downloaded += (_, endpoint) => Dispatcher.BeginInvoke(() =>
            StatusText.Text = L.T($"✓ Letöltve ({_server.Downloads}×) – {endpoint}", $"✓ Downloaded ({_server.Downloads}×) – {endpoint}"));

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
        Closed += (_, _) => _server.Dispose();
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
        if (AddressBox.SelectedItem is LocalShareServer.LocalAddress address) ShowUrl(_server.UrlFor(address.Address));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (UrlBox.Text.Length > 0) Clipboard.SetText(UrlBox.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
