using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GRWBetterMovementLauncher.Services;

internal sealed record SaveLocationInfo(
    GameInstallation Installation,
    string Id,
    string Name,
    string Path,
    string SaveFilesPath,
    bool IsAutomatic,
    bool IsReady,
    string Status,
    string? LatestBackupPath,
    bool CanBackUp = true,
    string BackUpToolTip = "Creates a new timestamped backup of this save location.")
{
    public string DisplayName => $"{Installation.Storefront} · {Name}";
    public bool HasBackup => !string.IsNullOrWhiteSpace(LatestBackupPath);
}

internal sealed record SaveBackupSummary(bool HasSources, bool IsReady, string Detail, string ToolTip);

internal static class SaveBackupService
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GRW Analogue Movement Mod");
    private static readonly string CustomLocationsFile = Path.Combine(StateDirectory, "custom-save-locations.txt");
    private static readonly string RecordsFile = Path.Combine(StateDirectory, "save-backup-records.json");
    private static readonly string LegacyRecordFile = Path.Combine(StateDirectory, "latest-save-backup.txt");

    public static IReadOnlyList<SaveLocationInfo> GetLocations(GameInstallation installation)
    {
        List<SaveLocation> locations = DiscoverLocations(installation);
        Dictionary<string, BackupRecord> records = LoadRecords();
        return locations.Select(location => ToInfo(installation, location, records)).ToArray();
    }

    public static SaveBackupSummary GetSummary(GameInstallation installation)
    {
        IReadOnlyList<SaveLocationInfo> locations = GetLocations(installation);
        if (locations.Count == 0)
            return new(false, false, $"No {installation.Storefront} save data was detected yet.",
                "Open Manage to add a custom save location or create save data for this edition.");

        int ready = locations.Count(location => location.IsReady);
        if (ready != locations.Count)
            return new(true, false, $"{ready}/{locations.Count} save location{(locations.Count == 1 ? "" : "s")} backed up for {installation.Storefront}.",
                string.Join(Environment.NewLine, locations.Select(location => $"{location.Name}: {location.Status}")));

        return new(true, true, "Save game backups for this game installation are current.",
            string.Join(Environment.NewLine, locations.Select(location => $"{location.Name}: {location.Status}")));
    }

    public static void AddCustomLocation(GameInstallation installation, string path)
    {
        string canonical = CanonicalPath(path);
        IReadOnlyList<SaveLocation> candidates = CreateLocations(installation, canonical, false);
        if (candidates.Count == 0)
            throw new DirectoryNotFoundException($"No {installation.Storefront} Ghost Recon Wildlands save containers were found in that location.");

        foreach (SaveLocation existing in DiscoverLocations(installation))
        {
            if (candidates.Any(candidate => existing.CanonicalPath.Equals(candidate.CanonicalPath, StringComparison.OrdinalIgnoreCase) ||
                existing.Containers.Select(item => item.CanonicalPath).Intersect(candidate.Containers.Select(item => item.CanonicalPath), StringComparer.OrdinalIgnoreCase).Any()))
                throw new InvalidOperationException("That location duplicates or overlaps an existing save source.");
        }

        Directory.CreateDirectory(StateDirectory);
        List<string> paths = LoadCustomPaths().ToList();
        paths.Add(canonical);
        File.WriteAllLines(CustomLocationsFile, paths.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static void RemoveCustomLocation(GameInstallation installation, string id)
    {
        SaveLocation? selected = DiscoverLocations(installation).FirstOrDefault(location => location.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (selected is null || selected.IsAutomatic) return;
        List<string> retained = LoadCustomPaths()
            .Where(path => !CanonicalPath(path).Equals(selected.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllLines(CustomLocationsFile, retained);
    }

    public static string BackupLocation(GameInstallation installation, string locationId)
    {
        List<SaveLocation> locations = DiscoverLocations(installation);
        SaveLocation location = locations.FirstOrDefault(item => item.Id.Equals(locationId, StringComparison.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException("That save location is no longer available.");

        Dictionary<string, BackupRecord> records = LoadRecords();
        DateTimeOffset now = DateTimeOffset.Now;
        string edition = StorefrontKey(installation);
        string destination = Path.Combine(BackupHistoryDirectory(edition, location), now.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
        CopyDirectory(location.Containers[0].Path, destination);
        records[location.Id] = new BackupRecord(now, destination);
        SaveRecords(records);
        return destination;
    }

    private static SaveLocationInfo ToInfo(GameInstallation installation, SaveLocation location, Dictionary<string, BackupRecord> records)
    {
        BackupRecord? record = records.GetValueOrDefault(location.Id)
            ?? CompatibleGroupedRecord(installation, location, records)
            ?? CompatibleLegacyRecord(installation, location);
        if (record is null || !Directory.Exists(record.Destination))
            return new(installation, location.Id, location.Name, location.Path, SaveFilesDirectory(location), location.IsAutomatic, false, "No backup recorded", null);

        DateTime newestWriteUtc = location.Containers.SelectMany(container => Directory.GetFiles(container.Path, "*", SearchOption.AllDirectories))
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        bool current = newestWriteUtc <= record.BackedUpAt.UtcDateTime.AddSeconds(1);
        return new(installation, location.Id, location.Name, location.Path, SaveFilesDirectory(location), location.IsAutomatic, current,
            current ? $"Backed up {record.BackedUpAt.LocalDateTime:yyyy-MM-dd HH:mm}" : $"Changed since backup {record.BackedUpAt.LocalDateTime:yyyy-MM-dd HH:mm}",
            record.Destination);
    }

    private static List<SaveLocation> DiscoverLocations(GameInstallation installation)
    {
        List<SaveLocation> result = [];
        HashSet<string> containers = new(StringComparer.OrdinalIgnoreCase);
        string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "savegames");
        IEnumerable<(string Path, bool Automatic)> candidates = new[] { (standard, true) }
            .Concat(LoadCustomPaths().Select(path => (path, false)));
        foreach ((string path, bool automatic) in candidates)
        {
            IReadOnlyList<SaveLocation> locations;
            try { locations = CreateLocations(installation, path, automatic); }
            catch { continue; }
            foreach (SaveLocation location in locations)
            {
                SaveContainer[] unique = location.Containers.Where(container => containers.Add(container.CanonicalPath)).ToArray();
                if (unique.Length > 0) result.Add(location with { Containers = unique });
            }
        }
        return result;
    }

    private static IReadOnlyList<SaveLocation> CreateLocations(GameInstallation installation, string path, bool automatic)
    {
        if (!Directory.Exists(path)) return [];
        string canonical = CanonicalPath(path);
        HashSet<string> gameIds = SaveGameIdsFor(installation).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<SaveContainer> containers = [];
        DirectoryInfo selected = new(canonical);

        if (gameIds.Contains(selected.Name)) AddContainer(selected);
        foreach (DirectoryInfo child in selected.EnumerateDirectories())
        {
            if (gameIds.Contains(child.Name)) AddContainer(child);
            else foreach (DirectoryInfo grandchild in child.EnumerateDirectories().Where(item => gameIds.Contains(item.Name))) AddContainer(grandchild);
        }
        if (containers.Count == 0) return [];
        SaveContainer chosen = containers
            .OrderBy(container => ContainerPreference(installation, container.GameId))
            .ThenByDescending(NewestWriteUtc)
            .First();
        string name = automatic ? "Save game" : selected.Name;
        return [new SaveLocation(SourceId(installation, chosen.CanonicalPath), name, canonical, canonical, automatic, [chosen])];

        void AddContainer(DirectoryInfo directory)
        {
            string account = directory.Parent?.Name ?? "Unknown account";
            containers.Add(new SaveContainer(directory.FullName, CanonicalPath(directory.FullName), account, directory.Name));
        }
    }

    private static int ContainerPreference(GameInstallation installation, string gameId)
    {
        if (installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase))
            return gameId.Equals("3559", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return gameId.Equals("1771", StringComparison.OrdinalIgnoreCase) ? 0
            : gameId.Equals("4740", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static DateTime NewestWriteUtc(SaveContainer container)
    {
        try
        {
            return Directory.GetFiles(container.Path, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        }
        catch { return DateTime.MinValue; }
    }

    private static BackupRecord? CompatibleGroupedRecord(GameInstallation installation, SaveLocation location, Dictionary<string, BackupRecord> records)
    {
        BackupRecord? grouped = records.GetValueOrDefault(SourceId(installation, location.CanonicalPath));
        if (grouped is null || !Directory.Exists(grouped.Destination)) return null;
        string? container = Directory.EnumerateDirectories(grouped.Destination, location.Containers[0].GameId, SearchOption.AllDirectories).FirstOrDefault();
        return container is null ? null : grouped with { Destination = container };
    }

    private static BackupRecord? CompatibleLegacyRecord(GameInstallation installation, SaveLocation location)
    {
        if (!location.IsAutomatic || installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase) || !File.Exists(LegacyRecordFile)) return null;
        string record = File.ReadAllText(LegacyRecordFile).Trim();
        int driveMarker = record.IndexOf(@":\", StringComparison.Ordinal);
        if (driveMarker < 1) return null;
        string destination = record[(driveMarker - 1)..].Trim();
        if (!Directory.Exists(destination) || !location.Containers.Any(container => Directory.EnumerateDirectories(destination, container.GameId, SearchOption.AllDirectories).Any())) return null;
        if (!DateTime.TryParseExact(record[..Math.Min(16, record.Length)], "yyyy-MM-dd HH:mm", null,
                System.Globalization.DateTimeStyles.AssumeLocal, out DateTime time)) return null;
        string? container = Directory.EnumerateDirectories(destination, location.Containers[0].GameId, SearchOption.AllDirectories).FirstOrDefault();
        return container is null ? null : new BackupRecord(new DateTimeOffset(time), container);
    }

    private static Dictionary<string, BackupRecord> LoadRecords()
    {
        try { return File.Exists(RecordsFile) ? JsonSerializer.Deserialize<Dictionary<string, BackupRecord>>(File.ReadAllText(RecordsFile)) ?? [] : []; }
        catch { return []; }
    }

    private static void SaveRecords(Dictionary<string, BackupRecord> records)
    {
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllText(RecordsFile, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<string> LoadCustomPaths()
    {
        if (!File.Exists(CustomLocationsFile)) return [];
        return File.ReadAllLines(CustomLocationsFile).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim());
    }

    private static string[] SaveGameIdsFor(GameInstallation installation) =>
        installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase) ? ["3559"] : ["1771", "4740"];

    private static string StorefrontKey(GameInstallation installation) =>
        installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase) ? "Steam" : "Ubisoft Connect";

    private static string BackupHistoryDirectory(string edition, SaveLocation location) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Better Movement for KBM", "Save Backups",
        edition, $"{SafeName(location.Name)}-{location.Id[..8]}");

    private static string SaveFilesDirectory(SaveLocation location)
    {
        string[] paths = location.Containers.Select(container => container.Path).ToArray();
        if (paths.Length == 1) return paths[0];

        string common = Path.GetDirectoryName(paths[0]) ?? location.Path;
        while (!paths.All(path => path.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            string? parent = Path.GetDirectoryName(common);
            if (string.IsNullOrWhiteSpace(parent)) return location.Path;
            common = parent;
        }
        return common;
    }

    private static string CanonicalPath(string path)
    {
        DirectoryInfo directory = new(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        FileSystemInfo? target = directory.ResolveLinkTarget(true);
        return (target?.FullName ?? directory.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string SourceId(GameInstallation installation, string canonicalPath) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{StorefrontKey(installation)}|{canonicalPath}".ToUpperInvariant())));
    private static string SafeName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed record SaveLocation(string Id, string Name, string Path, string CanonicalPath, bool IsAutomatic, SaveContainer[] Containers);
    private sealed record SaveContainer(string Path, string CanonicalPath, string Account, string GameId);
    private sealed record BackupRecord(DateTimeOffset BackedUpAt, string Destination);
}
