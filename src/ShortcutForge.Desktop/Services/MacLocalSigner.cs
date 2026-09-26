using System.Diagnostics;
using ShortcutForge.Core.Localization;
using ShortcutForge.Signing;

namespace ShortcutForge.Desktop.Services;

/// <summary>
/// Signs on this Mac with Apple's built-in <c>shortcuts sign</c> command-line tool (macOS 12+).
/// Nothing leaves the computer; "Anyone" mode needs a signed-in iCloud account.
/// </summary>
public sealed class MacLocalSigner(string toolPath = MacLocalSigner.DefaultToolPath) : ISigner
{
    public const string DefaultToolPath = "/usr/bin/shortcuts";

    public static bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(DefaultToolPath);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public string DisplayName => L.T("Aláírás ezen a Macen (shortcuts sign)", "Sign on this Mac (shortcuts sign)");

    public bool ProducesSignedFile => true;

    public async Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, string shortcutName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(toolPath))
            throw new SigningException(L.T("Nincs 'shortcuts' parancs ezen a gépen (macOS 12 Monterey vagy újabb kell).",
                "There is no 'shortcuts' command on this computer (macOS 12 Monterey or later is required)."));

        var dir = Path.Combine(Path.GetTempPath(), "shortcutforge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "input.shortcut");
        var output = Path.Combine(dir, "signed.shortcut");
        try
        {
            await File.WriteAllBytesAsync(input, unsignedPlist, cancellationToken);

            var start = new ProcessStartInfo(toolPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "sign", "--mode", mode == SigningMode.Anyone ? "anyone" : "people-who-know-me", "--input", input, "--output", output })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start)
                                ?? throw new SigningException(L.T("A 'shortcuts' parancs nem indítható.", "Could not start the 'shortcuts' command."));
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
                cancellationToken.ThrowIfCancellationRequested();
                throw new SigningException(L.T("A 'shortcuts sign' nem fejeződött be időben.", "'shortcuts sign' did not finish in time."));
            }

            var message = ((await stdout) + (await stderr)).Trim();
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new SigningException(L.T($"A 'shortcuts sign' hibával állt le (kód {process.ExitCode}): {message}",
                    $"'shortcuts sign' failed (code {process.ExitCode}): {message}"));

            var signed = await File.ReadAllBytesAsync(output, cancellationToken);
            if (!SignedFile.IsSigned(signed))
                throw new SigningException(L.T("A 'shortcuts sign' kimenete nem aláírt shortcut.", "The output of 'shortcuts sign' is not a signed shortcut."));
            return signed;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ } catch (UnauthorizedAccessException) { }
        }
    }
}
