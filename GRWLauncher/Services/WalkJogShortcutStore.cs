using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace GRWBetterMovementLauncher.Services;

internal readonly record struct WalkJogShortcut(int? VirtualKey)
{
    public const int DefaultVirtualKey = 0x58;

    public static WalkJogShortcut Default => new(DefaultVirtualKey);
    public bool IsEnabled => VirtualKey.HasValue;

    public string DisplayName => VirtualKey is int virtualKey
        ? GetDisplayName(virtualKey)
        : "Disabled";

    private static string GetDisplayName(int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
    }
}

internal static class WalkJogShortcutStore
{
    private static readonly string ShortcutFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Better Movement for KBM",
        "walk-jog-shortcut.txt");

    public static string FilePath => ShortcutFile;

    public static WalkJogShortcut Load()
    {
        try
        {
            string value = File.ReadAllText(ShortcutFile).Trim();
            if (value.Equals("disabled", StringComparison.OrdinalIgnoreCase)) return new WalkJogShortcut(null);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int virtualKey)
                && virtualKey is > 0 and <= 0xFF)
                return new WalkJogShortcut(virtualKey);
        }
        catch
        {
            // A missing or damaged convenience setting falls back to the established default.
        }
        return WalkJogShortcut.Default;
    }

    public static bool TrySave(WalkJogShortcut shortcut)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutFile)!);
            string value = shortcut.VirtualKey?.ToString(CultureInfo.InvariantCulture) ?? "disabled";
            File.WriteAllText(ShortcutFile, value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
