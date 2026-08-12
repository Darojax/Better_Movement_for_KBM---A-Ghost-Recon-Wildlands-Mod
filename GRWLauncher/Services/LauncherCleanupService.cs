using System.IO;

namespace GRWBetterMovementLauncher.Services;

internal static class LauncherCleanupService
{
    public static void RemoveLauncherData()
    {
        int exitCode = FirewallService.RunElevated("remove-all");
        if (exitCode != 0) throw new InvalidOperationException($"The firewall cleanup helper failed with exit code {exitCode}.");

        string documentsRoot = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        string backupRoot = Path.GetFullPath(Path.Combine(documentsRoot, "Better Movement for KBM", "Save Backups"));
        string legacyBackupRoot = Path.GetFullPath(Path.Combine(documentsRoot, "GRW Movement Mod", "Save Backups"));
        string localRoot = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string runtimeState = Path.GetFullPath(Path.Combine(localRoot, "GRW Analogue Movement Mod"));
        string launcherSettings = Path.GetFullPath(Path.Combine(localRoot, "Better Movement for KBM"));

        DeleteOwnedDirectory(backupRoot, Path.Combine(documentsRoot, "Better Movement for KBM"));
        DeleteOwnedDirectory(legacyBackupRoot, Path.Combine(documentsRoot, "GRW Movement Mod"));
        DeleteOwnedDirectory(runtimeState, localRoot);
        DeleteOwnedDirectory(launcherSettings, localRoot);

        string backupParent = Path.GetDirectoryName(backupRoot)!;
        if (Directory.Exists(backupParent) && !Directory.EnumerateFileSystemEntries(backupParent).Any())
            Directory.Delete(backupParent);
        string legacyBackupParent = Path.GetDirectoryName(legacyBackupRoot)!;
        if (Directory.Exists(legacyBackupParent) && !Directory.EnumerateFileSystemEntries(legacyBackupParent).Any())
            Directory.Delete(legacyBackupParent);
    }

    private static void DeleteOwnedDirectory(string target, string requiredParent)
    {
        string fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        string fullParent = Path.GetFullPath(requiredParent).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullTarget.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cleanup refused an unexpected path: {fullTarget}");
        if (Directory.Exists(fullTarget)) Directory.Delete(fullTarget, recursive: true);
    }
}
