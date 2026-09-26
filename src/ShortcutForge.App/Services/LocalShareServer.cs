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

    /// <summary>Starts serving <paramref name="content"/> as <paramref name="fileName"/> (port 0 = any free port).</summary>
    public static LocalShareServer Start(byte[] content, string fileName, int port = 0)
    {
        var server = new LocalShareServer(content, fileName, port);
        server._listener.Start();
        _ = server.AcceptLoopAsync();
        return server;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>URL path of the file, e.g. /3f9a…/My%20Shortcut.shortcut.</summary>
    public string Path => $"/{_token}/{Uri.EscapeDataString(_fileName)}";

    public string UrlFor(IPAddress address) => $"http://{address}:{Port}{Path}";

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
