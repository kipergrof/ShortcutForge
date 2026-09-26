using System.ComponentModel;
using System.Diagnostics;

namespace ShortcutForge.App.Services;

/// <summary>
/// Windows Firewall checks for Send to iPhone. On a network marked "Public" Windows blocks all
/// incoming connections, so the iPhone cannot reach the share server; this detects that and adds a
/// narrow allow rule (one TCP port, local subnet only) after a UAC prompt.
/// </summary>
public static class FirewallHelper
{
    public const string RuleName = "ShortcutForge - Send to iPhone";

    private const int NetFwIpProtocolTcp = 6;
    private const int NetFwIpProtocolAny = 256;
    private const int NetFwRuleDirIn = 1;
    private const int NetFwActionAllow = 1;

    /// <summary>
    /// True if incoming TCP on <paramref name="port"/> is allowed for the active network profiles
    /// (or the firewall is off), false if Windows would block it, null if it cannot be determined.
    /// </summary>
    public static bool? IsInboundAllowed(int port)
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return null;
            dynamic policy = Activator.CreateInstance(type)!;
            int profiles = policy.CurrentProfileTypes;

            var anyEnabled = false;
            foreach (var profile in new[] { 1, 2, 4 }) // domain, private, public
                if ((profiles & profile) != 0 && (bool)policy.FirewallEnabled[profile]) anyEnabled = true;
            if (!anyEnabled) return true;

            var exe = Environment.ProcessPath;
            foreach (dynamic rule in policy.Rules)
            {
                if (!(bool)rule.Enabled || (int)rule.Direction != NetFwRuleDirIn || (int)rule.Action != NetFwActionAllow) continue;
                if (((int)rule.Profiles & profiles) == 0) continue;
                int protocol = rule.Protocol;
                if (protocol != NetFwIpProtocolTcp && protocol != NetFwIpProtocolAny) continue;
                string? app = rule.ApplicationName;
                if (!string.IsNullOrEmpty(app) && !string.Equals(app, exe, StringComparison.OrdinalIgnoreCase)) continue;
                // Rules for one service or one Store app (package / per-user AppContainer rules such as
                // Game Bar's "any port") do not apply to this process even though they look open.
                if (!string.IsNullOrEmpty((string?)rule.serviceName) ||
                    !string.IsNullOrEmpty((string?)rule.LocalAppPackageId) ||
                    !string.IsNullOrEmpty((string?)rule.LocalUserOwner)) continue;
                if (!RemoteAllowsLan((string?)rule.RemoteAddresses)) continue;
                string? ports = protocol == NetFwIpProtocolAny ? "*" : rule.LocalPorts;
                if (PortMatches(ports, port)) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a rule's remote address scope lets a phone on the local network in: "*" or one of
    /// the LAN keywords. A rule for specific remote addresses is not counted. Exposed for tests.
    /// </summary>
    public static bool RemoteAllowsLan(string? remoteAddresses)
    {
        if (string.IsNullOrWhiteSpace(remoteAddresses)) return true;
        return remoteAddresses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(a => a == "*" || a.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Firewall port list syntax: "*", "80", "1000-2000", "80,443,8000-8100". Exposed for tests.</summary>
    public static bool PortMatches(string? ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports)) return false;
        foreach (var part in ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "*") return true;
            var range = part.Split('-');
            if (range.Length == 1 && int.TryParse(range[0], out var single) && single == port) return true;
            if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to) && port >= from && port <= to) return true;
        }
        return false;
    }

    /// <summary>The netsh commands that (re)create the rule. Exposed for tests.</summary>
    public static string RuleCommand(int port) =>
        $"/c netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & " +
        $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP " +
        $"localport={port} remoteip=localsubnet profile=any";

    /// <summary>
    /// Adds the allow rule with administrator rights (shows a UAC prompt).
    /// Returns false if the user declined or it failed.
    /// </summary>
    public static bool AllowPort(int port)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", RuleCommand(port))
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null) return false;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt cancelled
        }
    }
}
