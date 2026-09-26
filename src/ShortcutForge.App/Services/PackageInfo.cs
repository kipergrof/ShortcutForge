using System.Runtime.InteropServices;

namespace ShortcutForge.App.Services;

/// <summary>Whether the app runs as an MSIX package (the Microsoft Store version).</summary>
public static class PackageInfo
{
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>True for the Store version: updates then come from the Store, not from GitHub.</summary>
    public static bool IsPackaged { get; } = DetectPackaged();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    private static bool DetectPackaged()
    {
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false; // Windows 7 / 8: no packages
        }
    }
}
