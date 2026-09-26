using ShortcutForge.App.Services;

namespace ShortcutForge.App.Tests;

// IsInboundAllowed and AllowPort need COM and a UAC prompt, so only the pure parts are covered.
public class FirewallHelperTests
{
    [Theory]
    [InlineData("*", 47813, true)]            // any port
    [InlineData("47813", 47813, true)]        // single
    [InlineData("47813", 47814, false)]
    [InlineData("1000-2000", 1000, true)]     // range, inclusive on both ends
    [InlineData("1000-2000", 2000, true)]
    [InlineData("1000-2000", 1500, true)]
    [InlineData("1000-2000", 999, false)]
    [InlineData("1000-2000", 2001, false)]
    [InlineData("80,443,8000-8100", 443, true)]   // mixed list
    [InlineData("80,443,8000-8100", 8050, true)]
    [InlineData("80,443,8000-8100", 8101, false)]
    [InlineData(" 80 , 443 ", 443, true)]     // whitespace around entries
    [InlineData("RPC", 47813, false)]         // non-numeric keyword Windows also uses
    [InlineData("", 47813, false)]
    [InlineData("   ", 47813, false)]
    [InlineData(null, 47813, false)]
    public void Reads_the_firewall_port_list_syntax(string? ports, int port, bool expected)
        => Assert.Equal(expected, FirewallHelper.PortMatches(ports, port));

    [Fact]
    public void Rule_is_narrow_and_replaces_an_older_one()
    {
        var command = FirewallHelper.RuleCommand(47813);

        Assert.StartsWith("/c ", command);
        Assert.Contains($"name=\"{FirewallHelper.RuleName}\"", command);
        Assert.Contains("dir=in action=allow protocol=TCP", command);
        Assert.Contains("localport=47813", command);
        // The whole point is a narrow rule: this port, and only from the local network.
        Assert.Contains("remoteip=localsubnet", command);
        // Deleting first keeps the rule from piling up when the port changes.
        Assert.True(command.IndexOf("delete rule", StringComparison.Ordinal)
                  < command.IndexOf("add rule", StringComparison.Ordinal));
    }
}
