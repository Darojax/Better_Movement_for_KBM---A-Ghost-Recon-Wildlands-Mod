using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace GRWBetterMovementLauncher.Services;

internal sealed record FirewallStatus(int Existing, int Blocked, int Managed);

internal static class FirewallService
{
    private const string RulePrefix = "Better Movement for KBM - ";
    private const string LegacyUbisoftRulePrefix = "GRW Isolation - Ubisoft";
    private const string LegacyProjectRulePrefix = "GRW Movement Mod - ";

    public static string[] GamePrograms(string gameDirectory) =>
    [
        Path.Combine(gameDirectory, "GRW.exe"),
        Path.Combine(gameDirectory, "GRW_Upp.exe"),
        Path.Combine(gameDirectory, "rungame.exe")
    ];

    public static string[] UbisoftPrograms()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher");
        return new[] { "UbisoftConnect.exe", "UbisoftExtension.exe", "UbisoftGameLauncher.exe", "UbisoftGameLauncher64.exe", "upc.exe", "UplayService.exe", "UplayWebCore.exe", "UbisoftWebCore.exe" }
            .Select(name => Path.Combine(root, name)).ToArray();
    }

    public static FirewallStatus Inspect(IEnumerable<string> programs)
    {
        string[] existing = programs.Where(File.Exists).Select(Path.GetFullPath).ToArray();
        if (existing.Length == 0) return new FirewallStatus(0, 0, 0);
        HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> managed = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
            foreach (dynamic rule in policy.Rules)
            {
                string? application = rule.ApplicationName as string;
                if (!(bool)rule.Enabled || (int)rule.Direction != 2 || (int)rule.Action != 0 || string.IsNullOrWhiteSpace(application)) continue;
                string full;
                try { full = Path.GetFullPath(application); } catch { continue; }
                if (!existing.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;
                blocked.Add(full);
                string ruleName = (string)rule.Name;
                if (ruleName.StartsWith(RulePrefix, StringComparison.Ordinal)
                    || ruleName.StartsWith(LegacyUbisoftRulePrefix, StringComparison.Ordinal))
                    managed.Add(full);
            }
        }
        catch { }
        return new FirewallStatus(existing.Length, blocked.Count, managed.Count);
    }

    public static int RunElevated(string operation, string? gameDirectory = null)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve the launcher executable.");
        string arguments = $"--firewall-helper {Quote(operation)}" + (gameDirectory is null ? "" : $" {Quote(gameDirectory)}");
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit();
            return process?.ExitCode ?? 1;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Administrator approval was cancelled.", exception);
        }
    }

    public static int ExecuteHelper(string operation, string? gameDirectory)
    {
        if (!IsAdministrator()) return 5;
        switch (operation)
        {
            case "install-game":
                if (gameDirectory is null || !File.Exists(Path.Combine(gameDirectory, "GRW.exe"))) return 2;
                ReplaceManagedRules("Game - ", GamePrograms(gameDirectory));
                return 0;
            case "remove-game":
                RemoveManagedRules("Game - ");
                return 0;
            case "remove-game-blocks":
                if (gameDirectory is null) return 2;
                RemoveBlockingRulesForPrograms(GamePrograms(gameDirectory));
                return 0;
            case "install-ubisoft":
                ReplaceManagedRules("Ubisoft - ", UbisoftPrograms());
                return 0;
            case "remove-ubisoft":
                RemoveManagedRules("Ubisoft - ");
                return 0;
            case "remove-ubisoft-blocks":
                RemoveBlockingRulesForPrograms(UbisoftPrograms());
                return 0;
            case "remove-all":
                RemoveManagedRules("Game - ");
                RemoveManagedRules("Ubisoft - ");
                RemoveRulesWithPrefix(LegacyProjectRulePrefix);
                return 0;
            default:
                return 2;
        }
    }

    private static void ReplaceManagedRules(string category, IEnumerable<string> programs)
    {
        string[] existingPrograms = programs.Where(File.Exists).Select(Path.GetFullPath).ToArray();
        if (category == "Game - ")
            RemoveManagedRulesForPrograms(existingPrograms);
        else
            RemoveManagedRules(category);

        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        foreach (string program in existingPrograms)
        {
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            rule.Name = RulePrefix + category + Path.GetFileName(program) + " - " + PathToken(program);
            rule.Description = "Created by Better Movement for KBM";
            rule.ApplicationName = Path.GetFullPath(program);
            rule.Direction = 2;
            rule.Action = 0;
            rule.Enabled = true;
            rule.Profiles = int.MaxValue;
            policy.Rules.Add(rule);
        }
    }

    private static void RemoveManagedRules(string category)
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        List<string> names = [];
        string prefix = RulePrefix + category;
        foreach (dynamic rule in policy.Rules)
        {
            string name = (string)rule.Name;
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                || (category == "Ubisoft - " && name.StartsWith(LegacyUbisoftRulePrefix, StringComparison.Ordinal)))
                names.Add(name);
        }
        foreach (string name in names) policy.Rules.Remove(name);
    }

    private static void RemoveManagedRulesForPrograms(IEnumerable<string> programs)
    {
        HashSet<string> targets = programs.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        List<string> names = [];
        foreach (dynamic rule in policy.Rules)
        {
            string name = (string)rule.Name;
            string? application = rule.ApplicationName as string;
            if (!name.StartsWith(RulePrefix + "Game - ", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(application)) continue;
            string full;
            try { full = Path.GetFullPath(application); } catch { continue; }
            if (targets.Contains(full)) names.Add(name);
        }
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase)) policy.Rules.Remove(name);
    }

    private static void RemoveRulesWithPrefix(string prefix)
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        List<string> names = [];
        foreach (dynamic rule in policy.Rules)
        {
            string name = (string)rule.Name;
            if (name.StartsWith(prefix, StringComparison.Ordinal)) names.Add(name);
        }
        foreach (string name in names) policy.Rules.Remove(name);
    }

    private static void RemoveBlockingRulesForPrograms(IEnumerable<string> programs)
    {
        HashSet<string> targets = programs.Where(File.Exists).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        List<string> names = [];
        foreach (dynamic rule in policy.Rules)
        {
            string? application = rule.ApplicationName as string;
            if (!(bool)rule.Enabled || (int)rule.Direction != 2 || (int)rule.Action != 0 || string.IsNullOrWhiteSpace(application)) continue;
            string full;
            try { full = Path.GetFullPath(application); } catch { continue; }
            if (targets.Contains(full)) names.Add((string)rule.Name);
        }
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase)) policy.Rules.Remove(name);
    }

    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static string PathToken(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..12];
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
