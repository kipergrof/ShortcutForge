using Renci.SshNet;
using Renci.SshNet.Common;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Signing;

public sealed record MacSshSettings
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string User { get; init; } = "";

    /// <summary>Password; used when <see cref="PrivateKeyPath"/> is empty.</summary>
    public string? Password { get; init; }

    /// <summary>Path of an OpenSSH private key file.</summary>
    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphrase { get; init; }
}

/// <summary>
/// Signs shortcuts on a Mac over SSH with the built-in <c>shortcuts sign</c> command (macOS 12+).
/// The Mac needs Remote Login enabled and, for "Anyone" mode, a signed-in iCloud account.
/// </summary>
public sealed class MacSshSigner(MacSshSettings settings) : ISigner
{
    public string DisplayName => L.T($"Mac aláírás SSH-n ({settings.User}@{settings.Host})", $"Mac signing over SSH ({settings.User}@{settings.Host})");

    public bool ProducesSignedFile => true;

    public async Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, string shortcutName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            throw new SigningException(L.T("Add meg a Mac címét és a felhasználónevet a beállításokban.", "Enter the Mac's address and user name in the settings."));

        return await Task.Run(() => Sign(unsignedPlist, mode, cancellationToken), cancellationToken);
    }

    private ConnectionInfo CreateConnectionInfo()
    {
        AuthenticationMethod auth;
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            var key = string.IsNullOrEmpty(settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            auth = new PrivateKeyAuthenticationMethod(settings.User, key);
        }
        else
        {
            auth = new PasswordAuthenticationMethod(settings.User, settings.Password ?? "");
        }
        return new ConnectionInfo(settings.Host, settings.Port, settings.User, auth) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private byte[] Sign(byte[] unsignedPlist, SigningMode mode, CancellationToken cancellationToken)
    {
        var info = CreateConnectionInfo();
        var id = Guid.NewGuid().ToString("N");
        var input = $"/tmp/shortcutforge-{id}.shortcut";
        var output = $"/tmp/shortcutforge-{id}-signed.shortcut";
        var modeArg = mode == SigningMode.Anyone ? "anyone" : "people-who-know-me";

        try
        {
            using var sftp = new SftpClient(info);
            using var ssh = new SshClient(info);
            sftp.Connect();
            ssh.Connect();
            try
            {
                using (var stream = new MemoryStream(unsignedPlist))
                    sftp.UploadFile(stream, input);
                cancellationToken.ThrowIfCancellationRequested();

                using var command = ssh.CreateCommand(
                    $"/usr/bin/shortcuts sign --mode {modeArg} --input '{input}' --output '{output}' 2>&1");
                command.CommandTimeout = TimeSpan.FromMinutes(2);
                var result = command.Execute();
                if (command.ExitStatus != 0 || !sftp.Exists(output))
                    throw new SigningException(
                        L.T($"A 'shortcuts sign' hibával állt le (kód {command.ExitStatus}): {result.Trim()}", $"'shortcuts sign' failed (code {command.ExitStatus}): {result.Trim()}"));

                using var signed = new MemoryStream();
                sftp.DownloadFile(output, signed);
                var bytes = signed.ToArray();
                if (!SignedFile.IsSigned(bytes))
                    throw new SigningException(L.T("A Mac által visszaadott fájl nem aláírt shortcut.", "The file returned by the Mac is not a signed shortcut."));
                return bytes;
            }
            finally
            {
                TryDelete(sftp, input);
                TryDelete(sftp, output);
            }
        }
        catch (SshAuthenticationException ex)
        {
            throw new SigningException(L.T("Sikertelen SSH bejelentkezés a Macre. Ellenőrizd a felhasználót és a jelszót/kulcsot.", "SSH login to the Mac failed. Check the user name and password/key."), ex);
        }
        catch (SshConnectionException ex)
        {
            throw new SigningException(L.T($"Nem sikerült csatlakozni: {ex.Message}", $"Could not connect: {ex.Message}"), ex);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            throw new SigningException(L.T($"A Mac nem érhető el ({settings.Host}:{settings.Port}): {ex.Message}. ", $"The Mac is not reachable ({settings.Host}:{settings.Port}): {ex.Message}. ") +
                                       L.T("Be van kapcsolva a Távoli bejelentkezés (Rendszerbeállítások › Általános › Megosztás)?", "Is Remote Login enabled (System Settings › General › Sharing)?"), ex);
        }
    }

    private static void TryDelete(SftpClient sftp, string path)
    {
        try
        {
            if (sftp.IsConnected && sftp.Exists(path)) sftp.DeleteFile(path);
        }
        catch (SshException)
        {
            // best effort cleanup
        }
    }

    /// <summary>Checks the connection and that the shortcuts CLI exists.</summary>
    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() =>
        {
            try
            {
                using var ssh = new SshClient(CreateConnectionInfo());
                ssh.Connect();
                using var cmd = ssh.CreateCommand("sw_vers -productVersion; test -x /usr/bin/shortcuts && echo OK");
                var output = cmd.Execute().Trim();
                if (!output.EndsWith("OK"))
                    throw new SigningException(L.T("A Macen nincs 'shortcuts' parancs (macOS 12 Monterey vagy újabb kell).", "The Mac has no 'shortcuts' command (macOS 12 Monterey or later is required)."));
                return L.T("Kapcsolat rendben, macOS ", "Connection OK, macOS ") + output.Split('\n')[0].Trim();
            }
            catch (SshAuthenticationException ex)
            {
                throw new SigningException(L.T("Sikertelen SSH bejelentkezés.", "SSH login failed."), ex);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                throw new SigningException(L.T($"A Mac nem érhető el: {ex.Message}", $"The Mac is not reachable: {ex.Message}"), ex);
            }
        }, cancellationToken);
}
