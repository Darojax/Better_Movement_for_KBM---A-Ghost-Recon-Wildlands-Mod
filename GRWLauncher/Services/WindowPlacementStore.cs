using System.Globalization;
using System.IO;

namespace GRWBetterMovementLauncher.Services;

internal readonly record struct SavedWindowPosition(double Left, double Top);

internal static class WindowPlacementStore
{
    private static readonly string PositionFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Better Movement for KBM",
        "launcher-position.txt");

    public static SavedWindowPosition? Load()
    {
        try
        {
            string[] values = File.ReadAllLines(PositionFile);
            if (values.Length < 2
                || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double left)
                || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double top)
                || !double.IsFinite(left)
                || !double.IsFinite(top))
                return null;
            return new SavedWindowPosition(left, top);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(double left, double top)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PositionFile)!);
            File.WriteAllLines(PositionFile,
            [
                left.ToString("R", CultureInfo.InvariantCulture),
                top.ToString("R", CultureInfo.InvariantCulture)
            ]);
        }
        catch
        {
            // Window placement is a convenience and must never interfere with shutdown.
        }
    }
}
