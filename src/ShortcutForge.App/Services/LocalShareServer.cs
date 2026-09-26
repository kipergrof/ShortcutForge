using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ShortcutForge.App.Services;

/// <summary>
/// Minimal HTTP server that offers one file on the local network, so an iPhone can download a
/// shortcut by scanning a QR code (there is no AirDrop on Windows). It needs no admin rights,
/// serves only a random unguessable path, and stops when disposed.
/// </summary>
public sealed class LocalShareServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _content;
    private readonly string _fileName;
    private readonly string _token;
    private int _downloads;

    private LocalShareServer(byte[] content, string fileName, int port)
    {
        _content = content;
        _fileName = fileName;
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        _listener = new TcpListener(IPAddress.Any, port);
    }

    /// <summary>
    /// Fixed port tried first, so one firewall rule (see <see cref="FirewallHelper"/>) keeps working
    /// across launches. Falls back to any free port when it is taken.
    /// </summary>
    public const int PreferredPort = 47813;

    /// <summary>
    /// Starts serving <paramref name="content"/> as <paramref name="fileName"/>.
    /// Port -1 = <see cref="PreferredPort"/> or any free port; 0 = any free port.
    /// </summary>
    public static LocalShareServer Start(byte[] content, string fileName, int port = -1)
    {
        LocalShareServer server;
        if (port == -1)
        {
            server = new LocalShareServer(content, fileName, PreferredPort);
            try
            {
                server._listener.Start();
            }
            catch (SocketException)
            {
                server.Dispose();
                server = new LocalShareServer(content, fileName, 0);
                server._listener.Start();
            }
        }
        else
        {
            server = new LocalShareServer(content, fileName, port);
            server._listener.Start();
        }
        _ = server.AcceptLoopAsync();
        return server;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>URL path of the file, e.g. /3f9a…/My%20Shortcut.shortcut.</summary>
    public string Path => $"/{_token}/{Uri.EscapeDataString(_fileName)}";

    public string UrlFor(IPAddress address) => $"http://{address}:{Port}{Path}";

    /// <summary>Landing page with a download button and instructions (what the QR code opens).</summary>
    public string PagePath => $"/{_token}/";

    public string PageUrlFor(IPAddress address) => $"http://{address}:{Port}{PagePath}";

    /// <summary>Raised (on a worker thread) when a device opened the landing page: the connection works.</summary>
    public event EventHandler<EndPoint?>? PageOpened;

    public int Downloads => _downloads;

    /// <summary>Raised (on a worker thread) after the file was sent to a device.</summary>
    public event EventHandler<EndPoint?>? Downloaded;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var stream = client.GetStream();
                var request = await ReadRequestHeadAsync(stream, timeout.Token);
                if (request is null) return;

                var parts = request.Split('\n')[0].Trim().Split(' ');
                var method = parts.Length > 0 ? parts[0] : "";
                var target = parts.Length > 1 ? parts[1].Split('?')[0] : "";

                if (method is not ("GET" or "HEAD"))
                {
                    await WriteAsync(stream, "405 Method Not Allowed", "text/plain", "Method not allowed"u8.ToArray(), headOnly: false, timeout.Token);
                    return;
                }
                if (target == PagePath || target == PagePath.TrimEnd('/'))
                {
                    await WriteAsync(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(LandingPage()), method == "HEAD", timeout.Token);
                    if (method == "GET") PageOpened?.Invoke(this, client.Client.RemoteEndPoint);
                    return;
                }
                if (!IsFilePath(target))
                {
                    await WriteAsync(stream, "404 Not Found", "text/plain", "Not found"u8.ToArray(), method == "HEAD", timeout.Token);
                    return;
                }

                var ascii = new string(_fileName.Select(c => c < 128 && c != '"' ? c : '_').ToArray());
                var extra = $"Content-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(_fileName)}\r\n";
                await WriteAsync(stream, "200 OK", "application/octet-stream", _content, method == "HEAD", timeout.Token, extra);
                if (method == "GET")
                {
                    Interlocked.Increment(ref _downloads);
                    Downloaded?.Invoke(this, client.Client.RemoteEndPoint);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // client went away or timed out
            }
        }
    }

    private string LandingPage()
    {
        var name = WebUtility.HtmlEncode(System.IO.Path.GetFileNameWithoutExtension(_fileName));
        // Rooted, not relative: the page is also served without its trailing slash, and a relative
        // href would then resolve to /<file> instead of /<token>/<file>.
        var href = WebUtility.HtmlEncode(Path);
        var download = WebUtility.HtmlEncode(_fileName);
        string T(string hu, string en) => WebUtility.HtmlEncode(Core.Localization.L.T(hu, en));
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{name}}</title>
            <style>
            body{font-family:-apple-system,system-ui,sans-serif;margin:0;padding:28px 20px;background:#f2f2f7;color:#111;text-align:center}
            .card{background:#fff;border-radius:16px;padding:24px 18px;max-width:420px;margin:0 auto;box-shadow:0 1px 3px rgba(0,0,0,.08)}
            h1{font-size:22px;margin:4px 0 18px}
            a.btn{display:block;background:#007aff;color:#fff;text-decoration:none;font-size:18px;font-weight:600;padding:15px;border-radius:12px}
            ol{text-align:left;font-size:15px;line-height:1.5;padding-left:22px;margin:20px 0 0;color:#333}
            small{display:block;margin-top:18px;color:#888}
            @media (prefers-color-scheme:dark){body{background:#000;color:#eee}.card{background:#1c1c1e}ol{color:#ccc} }
            </style></head>
            <body><div class="card">
            <div style="font-size:40px">⚡️</div>
            <h1>{{name}}</h1>
            <a class="btn" href="{{href}}" download="{{download}}">{{T("Letöltés", "Download")}}</a>
            <ol>
            <li>{{T("Koppints a Letöltés gombra, majd a felugró kérdésnél a Letöltésre.", "Tap Download, then Download again when asked.")}}</li>
            <li>{{T("Nyisd meg a letöltést (Safari: ↓ ikon a címsorban, vagy Fájlok app › Letöltések).", "Open the download (Safari: the ↓ icon in the address bar, or Files app › Downloads).")}}</li>
            <li>{{T("A Parancsok app megnyílik: koppints a Parancs hozzáadása gombra.", "The Shortcuts app opens: tap Add Shortcut.")}}</li>
            </ol>
            <small>ShortcutForge</small>
            </div></body></html>
            """;
    }

    private bool IsFilePath(string target)
    {
        var decoded = Uri.UnescapeDataString(target);
        return decoded == $"/{_token}/{_fileName}" || target == Path;
    }

    /// <summary>Reads up to the blank line ending the request head (max 16 KB).</summary>
    private static async Task<string?> ReadRequestHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) return null;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n") || text.Contains("\n\n")) return text;
        }
        return null;
    }

    private static async Task WriteAsync(NetworkStream stream, string status, string contentType, byte[] body,
        bool headOnly, CancellationToken cancellationToken, string extraHeaders = "")
    {
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                   $"Cache-Control: no-store\r\nConnection: close\r\n{extraHeaders}\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
        if (!headOnly) await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }

    // ------------------------------------------------------------------ addresses

    public sealed record LocalAddress(IPAddress Address, string InterfaceName)
    {
        public override string ToString() => $"{Address}  ({InterfaceName})";
    }

    private static readonly string[] VirtualAdapterHints =
        ["virtual", "vmware", "hyper-v", "vethernet", "virtualbox", "docker", "wsl", "npcap", "loopback", "tap-", "tunnel", "vpn", "bluetooth"];

    /// <summary>
    /// IPv4 addresses a phone on the same network can reach, best first: real Wi-Fi / Ethernet
    /// adapters with a default gateway before virtual adapters (VMware, Hyper-V, Docker, VPN…).
    /// </summary>
    public static IReadOnlyList<LocalAddress> GetLanAddresses()
    {
        var candidates = new List<(LocalAddress Address, int Score)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            IPInterfaceProperties properties;
            try { properties = nic.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var hasGateway = properties.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            var looksVirtual = VirtualAdapterHints.Any(h =>
                nic.Description.Contains(h, StringComparison.OrdinalIgnoreCase) ||
                nic.Name.Contains(h, StringComparison.OrdinalIgnoreCase));

            foreach (var unicast in properties.UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue; // link-local, no DHCP

                var score = RankAddress(hasGateway, looksVirtual, nic.NetworkInterfaceType, bytes);
                candidates.Add((new LocalAddress(ip, nic.Name), score));
            }
        }
        return candidates.OrderByDescending(c => c.Score).Select(c => c.Address).ToList();
    }

    /// <summary>Higher is better. Exposed for tests.</summary>
    public static int RankAddress(bool hasGateway, bool looksVirtual, NetworkInterfaceType type, byte[] ipv4)
    {
        var score = 0;
        if (hasGateway) score += 10;
        if (looksVirtual) score -= 20;
        if (type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet) score += 5;
        // Typical home / office LAN ranges.
        if (ipv4[0] == 192 && ipv4[1] == 168) score += 3;
        else if (ipv4[0] == 10 || (ipv4[0] == 172 && ipv4[1] is >= 16 and <= 31)) score += 2;
        return score;
    }
}
