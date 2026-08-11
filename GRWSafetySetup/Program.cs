using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

const string RulePrefix = "GRW Movement Mod - ";
const string GameName = "Tom Clancy's Ghost Recon Wildlands";

string? gameDirectory = FindGameDirectory();
if (args.Length > 0)
{
    string command = args[0].ToLowerInvariant();
    if (command is "--install-standard" or "--install-strict" or "--remove-firewall")
    {
        if (!IsAdministrator()) return RelaunchElevated(args);
        if (command == "--remove-firewall") RemoveManagedRules();
        else
        {
            if (gameDirectory is null) throw new DirectoryNotFoundException("Wildlands installation was not detected.");
            InstallFirewallRules(gameDirectory, command == "--install-strict");
        }
        return 0;
    }
    if (command == "--status") { PrintStatus(gameDirectory); return 0; }
    if (command == "--backup-saves") { BackupSaves(); return 0; }
}

Console.Title = "GRW Movement Mod - Safety Setup";
Console.WriteLine("GRW ANALOGUE MOVEMENT MOD - SAFETY SETUP");
Console.WriteLine("Offline single-player use only. No configuration guarantees protection from sanctions.\n");
while (true)
{
    PrintStatus(gameDirectory);
    Console.WriteLine("\n1. Install recommended GRW-only firewall rules");
    Console.WriteLine("2. Install strict rules (GRW + Ubisoft Connect)");
    Console.WriteLine("3. Remove rules created by this utility");
    Console.WriteLine("4. Back up Wildlands saves");
    Console.WriteLine("5. Refresh status");
    Console.WriteLine("0. Exit");
    Console.Write("Choice: ");
    switch (Console.ReadLine()?.Trim())
    {
        case "1": RunElevatedAndWait("--install-standard"); break;
        case "2":
            Console.Write("Strict mode can prevent Ubisoft Connect login, updates, and other Ubisoft games from working. Continue? [y/N]: ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                RunElevatedAndWait("--install-strict");
            break;
        case "3": RunElevatedAndWait("--remove-firewall"); break;
        case "4": BackupSaves(); break;
        case "5": break;
        case "0": return 0;
    }
    Console.WriteLine("\nPress Enter to continue."); Console.ReadLine(); Console.Clear();
    gameDirectory = FindGameDirectory();
}

static void PrintStatus(string? gameDirectory)
{
    Console.WriteLine("\nSAFETY STATUS");
    Status("Wildlands installation", gameDirectory is not null, gameDirectory ?? "Not detected");
    string sayNoDetail = "Cannot check";
    bool sayNo = gameDirectory is not null && DetectSayNoToEac(gameDirectory, out sayNoDetail);
    Status("SayNoToEAC heuristic", sayNo, gameDirectory is null ? "Cannot check" : sayNoDetail);
    string[] eacProcesses = Process.GetProcesses().Where(p => p.ProcessName.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("eac", StringComparison.OrdinalIgnoreCase)).Select(p => p.ProcessName).Distinct().ToArray();
    Status("EAC processes absent", eacProcesses.Length == 0, eacProcesses.Length == 0 ? "None detected" : string.Join(", ", eacProcesses));
    if (gameDirectory is not null)
    {
        string[] standardPrograms = GamePrograms(gameDirectory).Where(File.Exists).ToArray();
        int blocked = standardPrograms.Count(IsProgramBlocked);
        Status("GRW outbound isolation", blocked == standardPrograms.Length && standardPrograms.Length == 3, $"{blocked}/{standardPrograms.Length} executable rules active");
    }
    else Status("GRW outbound isolation", false, "Cannot check");
    int strictExisting = UbisoftPrograms().Where(File.Exists).Count(IsProgramBlocked);
    int strictTotal = UbisoftPrograms().Count(File.Exists);
    Status("Strict Ubisoft isolation", strictTotal > 0 && strictExisting == strictTotal, $"{strictExisting}/{strictTotal} detected executable rules active (optional)");
}

static void Status(string label, bool passed, string detail) => Console.WriteLine($"[{(passed ? "OK" : "!!")}] {label}: {detail}");

static string? FindGameDirectory()
{
    string[] registryPaths =
    [
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    ];
    foreach (string path in registryPaths)
    {
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(path);
        if (root is null) continue;
        foreach (string subName in root.GetSubKeyNames())
        {
            using RegistryKey? sub = root.OpenSubKey(subName);
            if (!string.Equals(sub?.GetValue("DisplayName") as string, GameName, StringComparison.OrdinalIgnoreCase)) continue;
            string? location = sub!.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(Path.Combine(location, "GRW.exe"))) return Path.GetFullPath(location);
        }
    }
    string[] common =
    [
        @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Ghost Recon Wildlands",
        @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Ghost Recon Wildlands"
    ];
    return common.FirstOrDefault(path => File.Exists(Path.Combine(path, "GRW.exe")));
}

static bool DetectSayNoToEac(string gameDirectory, out string detail)
{
    string eac = Path.Combine(gameDirectory, "EasyAntiCheat");
    string x64 = Path.Combine(eac, "EasyAntiCheat_x64.dll"), x64Backup = x64 + ".BAK";
    string x86 = Path.Combine(eac, "EasyAntiCheat_x86.dll"), x86Backup = x86 + ".BAK";
    bool stubs = Small(x64) && Small(x86);
    bool backups = Large(x64Backup) && Large(x86Backup);
    detail = stubs && backups ? "Stub DLLs and original .BAK files detected" : "Expected stub/backup layout not found";
    return stubs && backups;
    static bool Small(string path) => File.Exists(path) && new FileInfo(path).Length is > 0 and < 65536;
    static bool Large(string path) => File.Exists(path) && new FileInfo(path).Length > 262144;
}

static string[] GamePrograms(string gameDirectory) =>
[
    Path.Combine(gameDirectory, "GRW.exe"),
    Path.Combine(gameDirectory, "GRW_Upp.exe"),
    Path.Combine(gameDirectory, "rungame.exe")
];

static string[] UbisoftPrograms()
{
    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher");
    return new[] { "UbisoftConnect.exe", "UbisoftExtension.exe", "UbisoftGameLauncher.exe", "UbisoftGameLauncher64.exe", "upc.exe", "UplayService.exe", "UplayWebCore.exe", "UbisoftWebCore.exe" }.Select(name => Path.Combine(root, name)).ToArray();
}

static void InstallFirewallRules(string gameDirectory, bool strict)
{
    RemoveManagedRules();
    foreach (string program in GamePrograms(gameDirectory).Where(File.Exists)) AddBlockRule(RulePrefix + Path.GetFileName(program), program);
    if (strict) foreach (string program in UbisoftPrograms().Where(File.Exists)) AddBlockRule(RulePrefix + "Strict - " + Path.GetFileName(program), program);
    Console.WriteLine(strict ? "Strict isolation rules installed." : "Recommended GRW-only isolation rules installed.");
}

static void AddBlockRule(string name, string program)
{
    dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
    dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
    rule.Name = name; rule.Description = "Created by GRW Analogue Movement Mod Safety Setup";
    rule.ApplicationName = Path.GetFullPath(program); rule.Direction = 2; rule.Action = 0; rule.Enabled = true; rule.Profiles = int.MaxValue;
    policy.Rules.Add(rule);
}

static void RemoveManagedRules()
{
    dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
    List<string> names = [];
    foreach (dynamic rule in policy.Rules) if (((string)rule.Name).StartsWith(RulePrefix, StringComparison.Ordinal)) names.Add((string)rule.Name);
    foreach (string name in names) policy.Rules.Remove(name);
    Console.WriteLine($"Removed {names.Count} firewall rule(s) managed by this utility.");
}

static bool IsProgramBlocked(string program)
{
    string full = Path.GetFullPath(program);
    try
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        foreach (dynamic rule in policy.Rules)
        {
            string? app = rule.ApplicationName as string;
            if (rule.Enabled && (int)rule.Direction == 2 && (int)rule.Action == 0 && !string.IsNullOrWhiteSpace(app) && string.Equals(Path.GetFullPath(app!), full, StringComparison.OrdinalIgnoreCase)) return true;
        }
    }
    catch { }
    return false;
}

static void BackupSaves()
{
    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "savegames");
    if (!Directory.Exists(root)) { Console.WriteLine("Ubisoft save directory not found."); return; }
    string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GRW Movement Mod", "Save Backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
    int copied = 0;
    foreach (string account in Directory.GetDirectories(root))
        foreach (string gameId in new[] { "1771", "4740" })
        {
            string source = Path.Combine(account, gameId);
            if (!Directory.Exists(source)) continue;
            CopyDirectory(source, Path.Combine(destination, Path.GetFileName(account), gameId)); copied++;
        }
    Console.WriteLine(copied > 0 ? $"Backed up {copied} save folder(s) to:\n{destination}" : "No Wildlands save folders (1771/4740) were found.");
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
    foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
}

static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
static int RelaunchElevated(string[] arguments)
{
    try
    {
        using Process? child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = string.Join(' ', arguments.Select(Quote)) });
        child?.WaitForExit(); return child?.ExitCode ?? 1;
    }
    catch { Console.Error.WriteLine("Administrator approval was cancelled or failed."); return 1; }
}
static void RunElevatedAndWait(string argument)
{
    if (IsAdministrator())
    {
        using Process? child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, Arguments = argument }); child?.WaitForExit();
    }
    else RelaunchElevated([argument]);
}
static string Quote(string value) => value.Contains(' ') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
