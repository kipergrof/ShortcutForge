using ShortcutForge.Core.Localization;

namespace ShortcutForge.Signing;

/// <summary>Who can import the signed shortcut (<c>shortcuts sign --mode</c>).</summary>
public enum SigningMode
{
    /// <summary>Anyone can import it (signed with an Apple-issued certificate via iCloud).</summary>
    Anyone,

    /// <summary>Only people who have the signer in their contacts.</summary>
    PeopleWhoKnowMe,
}

/// <summary>Turns an unsigned shortcut plist into a file an iPhone / Mac can import.</summary>
public interface ISigner
{
    string DisplayName { get; }

    /// <summary>True if the result can be imported on iOS 15+ directly.</summary>
    bool ProducesSignedFile { get; }

    Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, CancellationToken cancellationToken = default);
}

/// <summary>Writes the binary plist as-is. Must be signed on a Mac before importing on iOS 15+.</summary>
public sealed class UnsignedExporter : ISigner
{
    public string DisplayName => L.T("Aláírás nélkül (Macen aláírandó)", "Unsigned (to be signed on a Mac)");

    public bool ProducesSignedFile => false;

    public Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(unsignedPlist);
}

public sealed class SigningException(string message, Exception? inner = null) : Exception(message, inner);

public static class SignedFile
{
    /// <summary>Signed shortcuts are Apple Encrypted Archives starting with "AEA1".</summary>
    public static bool IsSigned(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 'A' && data[1] == 'E' && data[2] == 'A' && data[3] == '1';
}
