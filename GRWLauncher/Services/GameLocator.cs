using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GRWBetterMovementLauncher.Services;

public sealed record GameInstallation(string Directory, string Storefront, string? BuildIdentifier = null)
{
    public string Executable => Path.Combine(Directory, "GRW.exe");
}

internal static partial class GameLocator
{
    private const string GameName = "Tom Clancy's Ghost Recon Wildlands";
    private const string SteamAppId = "460930";
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Better Movement for KBM");
    private static readonly string SelectionFile = Path.Combine(SettingsDirectory, "selected-game-path.txt");

    public static GameInstallation? Find()
    {
        GameInstallation? running = FromRunningProcess();
        if (running is not null) return running;

        if (File.Exists(SelectionFile))
        {
            string selected = File.ReadAllText(SelectionFile).Trim();
            GameInstallation? saved = FromDirectory(selected);
            if (saved is not null) return saved;
        }

        return FindAll().FirstOrDefault();
    }

    public static IReadOnlyList<GameInstallation> FindAll()
    {
        List<GameInstallation> installations = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameInstallation installation in FindSteamInstallations().Concat(FindRegistryInstallations()))
        {
            if (paths.Add(installation.Directory)) installations.Add(installation);
        }
        return installations;
    }

    public static GameInstallation? FromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        string full;
        try { full = Path.GetFullPath(directory); }
        catch { return null; }
        if (!File.Exists(Path.Combine(full, "GRW.exe"))) return null;
        string storefront = full.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ? "Steam" : "Ubisoft Connect";
        string? buildIdentifier = storefront == "Steam" ? ReadSteamBuildIdentifier(full) : null;
        return new GameInstallation(full.TrimEnd(Path.DirectorySeparatorChar), storefront, buildIdentifier);
    }

    public static void SaveSelection(string directory)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SelectionFile, Path.GetFullPath(directory));
    }

    private static GameInstallation? FromRunningProcess()
    {
        foreach (Process process in Process.GetProcessesByName("GRW"))
        {
            using (process)
            {
                try
                {
                    string? executable = process.MainModule?.FileName;
                    if (executable is not null) return FromDirectory(Path.GetDirectoryName(executable)!);
                }
                catch { }
            }
        }
        return null;
    }

    private static IEnumerable<GameInstallation> FindRegistryInstallations()
    {
        (RegistryKey Root, string Path)[] roots =
        [
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        ];

        foreach ((RegistryKey registryRoot, string path) in roots)
        {
            using RegistryKey? root = registryRoot.OpenSubKey(path);
            if (root is null) continue;
            foreach (string subName in root.GetSubKeyNames())
            {
                using RegistryKey? sub = root.OpenSubKey(subName);
                if (!string.Equals(sub?.GetValue("DisplayName") as string, GameName, StringComparison.OrdinalIgnoreCase)) continue;
                string? location = sub?.GetValue("InstallLocation") as string;
                GameInstallation? installation = location is null ? null : FromDirectory(location);
                if (installation is not null) yield return installation;
            }
        }

        string[] common =
        [
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Ghost Recon Wildlands",
            @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Ghost Recon Wildlands"
        ];
        foreach (string path in common)
        {
            GameInstallation? installation = FromDirectory(path);
            if (installation is not null) yield return installation;
        }
    }

    private static IEnumerable<GameInstallation> FindSteamInstallations()
    {
        string? steamRoot = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamRoot)) yield break;

        HashSet<string> libraries = new(StringComparer.OrdinalIgnoreCase) { steamRoot };
        string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            foreach (Match match in QuotedPathRegex().Matches(File.ReadAllText(libraryFile)))
                libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }

        foreach (string library in libraries)
        {
            string manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;
            Match installDir = InstallDirRegex().Match(File.ReadAllText(manifest));
            if (!installDir.Success) continue;
            string directory = Path.Combine(library, "steamapps", "common", installDir.Groups[1].Value);
            GameInstallation? installation = FromDirectory(directory);
            Match buildId = BuildIdRegex().Match(File.ReadAllText(manifest));
            string? identifier = buildId.Success ? $"Steam build {buildId.Groups[1].Value}" : null;
            if (installation is not null) yield return installation with { Storefront = "Steam", BuildIdentifier = identifier };
        }
    }

    private static string? ReadSteamBuildIdentifier(string gameDirectory)
    {
        string manifest = Path.GetFullPath(Path.Combine(gameDirectory, "..", "..", $"appmanifest_{SteamAppId}.acf"));
        if (!File.Exists(manifest)) return null;
        Match buildId = BuildIdRegex().Match(File.ReadAllText(manifest));
        return buildId.Success ? $"Steam build {buildId.Groups[1].Value}" : null;
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedPathRegex();

    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\\\"buildid\\\"\\s+\\\"(\\d+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex BuildIdRegex();
}
