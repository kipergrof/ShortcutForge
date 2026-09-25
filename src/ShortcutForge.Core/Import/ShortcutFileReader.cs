using System.Text;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Import;

/// <summary>Opens any shortcut file: unsigned binary/XML plist or a signed (AEA) .shortcut.</summary>
public static class ShortcutFileReader
{
    public static Shortcut Read(byte[] data, string? name = null)
    {
        if (data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "AEA1")
            return PlistSerializer.Read(SignedShortcutExtractor.ExtractPlist(data), name);

        if (data.Length >= 6 && Encoding.ASCII.GetString(data, 0, 6) == "bplist")
            return PlistSerializer.Read(data, name);

        var head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 200)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (head.StartsWith("<?xml") || head.StartsWith("<plist") || head.StartsWith("<!DOCTYPE"))
            return PlistSerializer.Read(data, name);

        throw new FormatException(L.T("Ismeretlen fájlformátum: nem shortcut plist és nem aláírt .shortcut.", "Unknown file format: neither a shortcut plist nor a signed .shortcut."));
    }

    public static Shortcut ReadFile(string path) =>
        Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
}
