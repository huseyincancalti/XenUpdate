using System.Runtime.InteropServices;

namespace XenUpdate.Infrastructure.WindowsUpdate;

/// <summary>
/// Shared low-level helpers for Windows Update Agent (WUApiLib) COM interop, used by
/// <see cref="WindowsUpdateService"/> and <see cref="DriverUpdateService"/>. Access is
/// late-bound (dynamic) so no COM reference entry is needed in the project file.
/// </summary>
internal static class WuaComHelpers
{
    internal static void TrySetOnline(dynamic searcher)
    {
        try { searcher.Online = true; }
        catch { }
    }

    internal static void TrySetForceQuiet(dynamic installer)
    {
        try { installer.ForceQuiet = true; }
        catch { }
    }

    internal static void TryAcceptEula(dynamic update)
    {
        try
        {
            if (!SafeConvertToBool(update.EulaAccepted))
            {
                update.AcceptEula();
            }
        }
        catch
        {
            // Some updates do not expose EULA state consistently; safe to continue.
        }
    }

    internal static int SafeConvertToInt(object? value)
    {
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    internal static bool SafeConvertToBool(object? value)
    {
        try { return Convert.ToBoolean(value); }
        catch { return false; }
    }

    internal static string SafeConvertToString(object? value) => value?.ToString() ?? string.Empty;

    internal static void TryReleaseCom(object? obj)
    {
        if (obj is null)
        {
            return;
        }

        try { Marshal.FinalReleaseComObject(obj); }
        catch { }
    }
}
