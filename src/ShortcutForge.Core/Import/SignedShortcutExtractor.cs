using System.Buffers.Binary;
using System.Text;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Import;

/// <summary>
/// Extracts the unsigned plist from a signed .shortcut file.
/// A signed shortcut is an Apple Encrypted Archive ("AEA1") using the signature-only profile:
/// the payload is not encrypted, just LZFSE-compressed segments of an Apple Archive ("AA01")
/// that contains Shortcut.wflow.
/// </summary>
public static class SignedShortcutExtractor
{
    public static byte[] ExtractPlist(byte[] file)
    {
        if (file.Length < 12 || Encoding.ASCII.GetString(file, 0, 4) != "AEA1")
            throw new FormatException(L.T("Ez nem aláírt shortcut (hiányzik az AEA1 fejléc).", "This is not a signed shortcut (missing AEA1 header)."));

        var authDataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8));
        var searchFrom = 12 + authDataSize;
        if (authDataSize < 0 || searchFrom > file.Length)
            throw new FormatException(L.T("Sérült aláírt shortcut (hibás auth data méret).", "Corrupt signed shortcut (invalid auth data size)."));

        // The segment data follows fixed-size signature/HMAC/header fields; find the first
        // LZFSE block and decode all consecutive segments.
        Exception? lastError = null;
        for (var pos = IndexOfBlock(file, searchFrom); pos >= 0; pos = IndexOfBlock(file, pos + 1))
        {
            try
            {
                var archive = DecodeSegments(file, pos);
                return ExtractWflow(archive);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException)
            {
                lastError = ex;
            }
        }
        throw new FormatException(L.T("Nem sikerült kicsomagolni az aláírt shortcutot. ", "Could not extract the signed shortcut. ") +
                                  L.T("Lehet, hogy titkosított (nem megosztásra aláírt) fájl.", "It may be an encrypted file (not signed for sharing)."), lastError);
    }

    private static int IndexOfBlock(byte[] data, int from)
    {
        for (var i = Math.Max(0, from); i + 4 <= data.Length; i++)
        {
            if (data[i] != 'b' || data[i + 1] != 'v' || data[i + 2] != 'x') continue;
            if (data[i + 3] is (byte)'2' or (byte)'n' or (byte)'-' or (byte)'1') return i;
        }
        return -1;
    }

    private static byte[] DecodeSegments(byte[] file, int pos)
    {
        using var output = new MemoryStream();
        while (pos < file.Length && IndexOfBlock(file, pos) == pos)
        {
            var segment = Lzfse.Decode(file.AsSpan(pos), out var consumed);
            output.Write(segment);
            pos += consumed;
        }
        return output.ToArray();
    }

    /// <summary>Finds Shortcut.wflow in an Apple Archive.</summary>
    public static byte[] ExtractWflow(byte[] archive)
    {
        if (archive.Length < 6 || Encoding.ASCII.GetString(archive, 0, 4) != "AA01")
            throw new FormatException(L.T("A kicsomagolt adat nem Apple Archive.", "The extracted data is not an Apple Archive."));

        byte[]? fallback = null;
        var pos = 0;
        while (pos + 6 <= archive.Length && Encoding.ASCII.GetString(archive, pos, 4) == "AA01")
        {
            var entry = ParseEntry(archive, pos);
            if (entry.Data is not null)
            {
                if (entry.Path is { } path && path.EndsWith(".wflow", StringComparison.OrdinalIgnoreCase))
                    return entry.Data;
                if (IsPlist(entry.Data)) fallback ??= entry.Data;
            }
            pos = entry.Next;
        }

        if (fallback is not null) return fallback;

        // Last resort: a binary plist running to the end of the archive.
        var start = IndexOf(archive, "bplist00"u8);
        if (start >= 0) return archive[start..];
        throw new FormatException(L.T("Az archívumban nincs Shortcut.wflow.", "The archive contains no Shortcut.wflow."));
    }

    private static bool IsPlist(byte[] data) =>
        data.AsSpan().StartsWith("bplist"u8) || data.AsSpan().StartsWith("<?xml"u8);

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern) => data.AsSpan().IndexOf(pattern);

    private readonly record struct Entry(string? Path, byte[]? Data, int Next);

    private static Entry ParseEntry(byte[] a, int start)
    {
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(start + 4));
        var headerEnd = start + headerSize;
        if (headerSize < 6 || headerEnd > a.Length) throw new FormatException(L.T("Sérült Apple Archive fejléc.", "Corrupt Apple Archive header."));

        string? path = null;
        var blobs = new List<(string Key, long Size)>();
        var p = start + 6;
        while (p + 4 <= headerEnd)
        {
            var key = Encoding.ASCII.GetString(a, p, 3);
            var type = (char)a[p + 3];
            p += 4;
            switch (type)
            {
                case '*': break;
                case '1': p += 1; break;
                case '2': p += 2; break;
                case '4': p += 4; break;
                case '8': p += 8; break;
                case 'A': blobs.Add((key, BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(p)))); p += 2; break;
                case 'B': blobs.Add((key, BinaryPrimitives.ReadUInt32LittleEndian(a.AsSpan(p)))); p += 4; break;
                case 'C': blobs.Add((key, (long)BinaryPrimitives.ReadUInt64LittleEndian(a.AsSpan(p)))); p += 8; break;
                case 'P':
                {
                    var len = BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(p));
                    var text = Encoding.UTF8.GetString(a, p + 2, len);
                    if (key == "PAT") path = text;
                    p += 2 + len;
                    break;
                }
                case 'S': p += 8; break;
                case 'T': p += 12; break;
                case 'F': p += 4; break;  // CRC32
                case 'G': p += 20; break; // SHA-1
                case 'H': p += 32; break; // SHA-256
                case 'I': p += 48; break; // SHA-384
                case 'J': p += 64; break; // SHA-512
                default:
                    // Unknown field type: the rest of the header cannot be parsed reliably.
                    throw new FormatException(L.T($"Ismeretlen Apple Archive mező: {key}{type}", $"Unknown Apple Archive field: {key}{type}"));
            }
        }

        byte[]? data = null;
        var blobPos = (long)headerEnd;
        foreach (var (key, size) in blobs)
        {
            if (blobPos + size > a.Length) throw new FormatException(L.T("Csonka Apple Archive bejegyzés.", "Truncated Apple Archive entry."));
            if (key == "DAT") data = a.AsSpan((int)blobPos, (int)size).ToArray();
            blobPos += size;
        }
        return new Entry(path, data, (int)blobPos);
    }
}
