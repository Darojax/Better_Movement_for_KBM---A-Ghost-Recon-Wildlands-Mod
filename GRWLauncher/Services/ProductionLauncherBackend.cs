using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GRWBetterMovementLauncher.Models;

namespace GRWBetterMovementLauncher.Services;

public sealed class ProductionLauncherBackend : ILauncherBackend, IDisposable
{
    private const string TestedExecutableHash = "56791FF5A6C213A77EEBEDAEAEE3026D63B70806071358CE96ABD3ED7947ADE7";
    private const string TestedSteamBuild = "24446260";
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GRW Analogue Movement Mod");
    private static readonly string AcceptanceFile = Path.Combine(StateDirectory, "offline-risk-accepted-v1");
    private static readonly string HashCacheFile = Path.Combine(StateDirectory, "executable-hash-cache.txt");

    private readonly SemaphoreSlim _inspectionLock = new(1, 1);
    private GameInstallation? _installation;
    private DateTime _installationCheckedAt;
    private FirewallStatus? _gameFirewall;
    private FirewallStatus? _ubisoftFirewall;
    private DateTime _firewallCheckedAt;
    private string? _hashedPath;
    private long _hashedLength;
    private DateTime _hashedWriteTimeUtc;
    private string? _executableHash;
    private Task<string>? _hashTask;
    private string? _hashTaskPath;
    private long _hashTaskLength;
    private DateTime _hashTaskWriteTimeUtc;
    private Process? _runtimeProcess;
    private EventWaitHandle? _shutdownEvent;
    private string _runtimeFailure = "";

    public bool RiskAcceptanceRequired => !File.Exists(AcceptanceFile);

    public async Task<LauncherSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        await _inspectionLock.WaitAsync(cancellationToken);
        try
        {
            RefreshInstallationIfNeeded();
            bool gameRunning = Process.GetProcessesByName("GRW").Length > 0;
            if (_runtimeProcess is { HasExited: true }) DisposeRuntimeHandles();
            bool runtimeActive = _runtimeProcess is { HasExited: false };
            bool foreignRuntime = Process.GetProcessesByName("GRWAnalogueMovement").Any(process =>
            {
                using (process) return _runtimeProcess is null || process.Id != _runtimeProcess.Id;
            });

            List<LauncherCheck> checks = [];
            if (_installation is null)
            {
                checks.Add(new("installation", "Game installation", "Ghost Recon Wildlands was not detected. Select the folder containing GRW.exe.", CheckStatus.Blocked, "Choose", "choose-game"));
                checks.Add(new("executable", "Game executable", "Cannot verify compatibility until the installation is selected.", CheckStatus.Blocked));
                checks.Add(new("saynotoeac", "Anti-cheat configuration", "Cannot inspect SayNoToEAC until the installation is selected.", CheckStatus.Blocked, "Manage", "manage-eac"));
            }
            else
            {
                checks.Add(new("installation", "Game installation", $"{_installation.Storefront} · {_installation.Directory}", CheckStatus.Ready, "Change", "choose-game"));
                checks.Add(BuildExecutableCheck(_installation));
                checks.Add(BuildSayNoToEacCheck(_installation.Directory));
            }

            string[] eacProcesses = ActiveEacProcesses();
            checks.Add(eacProcesses.Length == 0
                ? new("eac", "Easy Anti-Cheat", "No active Easy Anti-Cheat process detected.", CheckStatus.Ready)
                : new("eac", "Easy Anti-Cheat", $"Active process detected: {string.Join(", ", eacProcesses)}.", CheckStatus.Blocked));

            RefreshFirewallIfNeeded();
            checks.Add(BuildFirewallCheck("grw-firewall", "Ghost Recon Wildlands network isolation", _gameFirewall, "game"));
            checks.Add(BuildFirewallCheck("ubisoft-firewall", "Ubisoft Connect isolation", _ubisoftFirewall, "ubisoft"));

            checks.Add(foreignRuntime
                ? new("hooks", "Runtime hook state", "Another movement runtime is already running. Press F5 in that runtime before starting a new one.", CheckStatus.Blocked)
                : runtimeActive
                    ? new("hooks", "Runtime hook state", "Better Movement for KBM is attached and supervised by this launcher.", CheckStatus.Ready)
                    : gameRunning
                        ? new("hooks", "Runtime hook state", "Ghost Recon Wildlands is running; exact instructions will be verified before attachment.", CheckStatus.Ready, "Verify", "verify-hooks")
                        : new("hooks", "Runtime hook state", "Original instructions will be verified exactly when Ghost Recon Wildlands starts.", CheckStatus.Ready));

            checks.Add(BuildBackupCheck(_installation));
            string storefront = _installation?.Storefront ?? "Not detected";
            string path = _installation?.Directory ?? "Choose the folder containing GRW.exe";
            return new LauncherSnapshot(storefront, path, checks, gameRunning, runtimeActive);
        }
        finally
        {
            _inspectionLock.Release();
        }
    }

    public async Task<LauncherActionResult> ExecuteAsync(string actionId, CancellationToken cancellationToken = default)
    {
        switch (actionId)
        {
            case "choose-game":
                InstallationPickerWindow dialog = new(GameLocator.FindAll(), _installation?.Directory)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                if (dialog.ShowDialog() != true || dialog.SelectedInstallation is null)
                    return new LauncherActionResult("Game selection cancelled.", ShouldLog: false);
                GameInstallation selected = dialog.SelectedInstallation;
                GameLocator.SaveSelection(selected.Directory);
                InvalidateCaches();
                return new LauncherActionResult($"Selected {selected.Storefront} installation: {selected.Directory}");

            case "install-grw-firewall":
            case "remove-grw-firewall":
                EnsureInstallation();
                RunFirewall(actionId == "install-grw-firewall" ? "install-game" : "remove-game", _installation!.Directory);
                return new LauncherActionResult(actionId.StartsWith("install", StringComparison.Ordinal) ? "Ghost Recon Wildlands isolation rules installed." : "Launcher-managed Ghost Recon Wildlands isolation rules removed.");

            case "install-ubisoft-firewall":
            case "remove-ubisoft-firewall":
                RunFirewall(actionId == "install-ubisoft-firewall" ? "install-ubisoft" : "remove-ubisoft");
                return new LauncherActionResult(actionId.StartsWith("install", StringComparison.Ordinal) ? "Ubisoft Connect isolation rules installed." : "Ubisoft Connect isolation rules uninstalled.");

            case "manage-saves":
                EnsureInstallation();
                if (System.Windows.Application.Current.MainWindow is not MainWindow saveWindow)
                    throw new InvalidOperationException("The launcher window is unavailable.");
                await saveWindow.ShowSaveManagementAsync(_installation!);
                return new LauncherActionResult("Save backup management panel closed.", ShouldLog: false);

            case "manage-eac":
                EnsureInstallation();
                if (System.Windows.Application.Current.MainWindow is not MainWindow mainWindow)
                    throw new InvalidOperationException("The launcher window is unavailable.");
                await mainWindow.ShowAntiCheatManagementAsync(
                    _installation!.Directory,
                    SayNoToEacAppearsInstalled(_installation.Directory));
                return new LauncherActionResult("SayNoToEAC management panel closed.", ShouldLog: false);

            case "show-signatures":
                EnsureInstallation();
                string hash = await GetExecutableHashAsync(_installation!.Executable, cancellationToken);
                return new LauncherActionResult(hash.Equals(TestedExecutableHash, StringComparison.OrdinalIgnoreCase)
                    ? $"{_installation.Storefront} executable matches the tested Steam public build {TestedSteamBuild}. SHA-256: {hash}"
                    : $"Unrecognized executable SHA-256: {hash}. Exact in-memory signatures remain mandatory before attachment.");

            case "verify-hooks":
                string details = await VerifyHooksAsync(cancellationToken);
                return new LauncherActionResult(details);

            default:
                return new LauncherActionResult("Checks refreshed.", ShouldLog: false);
        }
    }

    public async Task LaunchWithModAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        EnsureInstallation();
        if (Process.GetProcessesByName("GRW").Length == 0)
        {
            progress.Report($"Starting Ghost Recon Wildlands through {_installation!.Storefront}…");
            LaunchGame(_installation);
            progress.Report("Waiting for GRW.exe…");
            await WaitForGameAsync(cancellationToken);
        }
        await StartRuntimeAsync(progress, cancellationToken);
    }

    public Task LaunchVanillaAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        EnsureInstallation();
        if (Process.GetProcessesByName("GRW").Length > 0)
        {
            progress.Report("Ghost Recon Wildlands is already running.");
            return Task.CompletedTask;
        }
        progress.Report($"Starting Ghost Recon Wildlands through {_installation!.Storefront} without the mod…");
        LaunchGame(_installation);
        return Task.CompletedTask;
    }

    public async Task AttachAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (Process.GetProcessesByName("GRW").Length == 0) throw new InvalidOperationException("Ghost Recon Wildlands is not running.");
        await StartRuntimeAsync(progress, cancellationToken);
    }

    public async Task StopRuntimeAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (_runtimeProcess is null || _runtimeProcess.HasExited)
        {
            DisposeRuntimeHandles();
            progress.Report("The supervised runtime is already stopped.");
            return;
        }

        progress.Report("Restoring original Ghost Recon Wildlands instructions…");
        _shutdownEvent?.Set();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try { await _runtimeProcess.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The runtime did not confirm restoration. Press F5 while its process is active before closing Ghost Recon Wildlands.");
        }
        finally
        {
            if (_runtimeProcess.HasExited) DisposeRuntimeHandles();
        }
        progress.Report("Better Movement for KBM is disabled. Ghost Recon Wildlands remains open.");
    }

    public void RecordRiskAcceptance()
    {
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllText(AcceptanceFile, DateTimeOffset.UtcNow.ToString("O"));
    }

    private async Task StartRuntimeAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_runtimeProcess is { HasExited: false }) throw new InvalidOperationException("Better Movement for KBM is already active.");
        if (ActiveEacProcesses().Length > 0) throw new InvalidOperationException("Easy Anti-Cheat is active. The mod will not attach.");
        EnsureInstallation();
        if (!SayNoToEacAppearsInstalled(_installation!.Directory)) throw new InvalidOperationException("SayNoToEAC was not detected. The mod will not attach.");
        string runtime = ResolveRuntimeExecutable();
        string eventName = $"Local\\BetterMovement-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        _runtimeFailure = "";

        progress.Report("Verifying exact movement and ADS instructions…");
        ProcessStartInfo start = new(runtime)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"--shutdown-event={Quote(eventName)}"
        };
        _runtimeProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
        _runtimeProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.Contains("Verified movement", StringComparison.OrdinalIgnoreCase)) progress.Report("Movement runtime attached successfully."); };
        _runtimeProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _runtimeFailure = e.Data; };
        if (!_runtimeProcess.Start()) throw new InvalidOperationException("Could not start the movement runtime.");
        _runtimeProcess.BeginOutputReadLine();
        _runtimeProcess.BeginErrorReadLine();
        await Task.Delay(1200, cancellationToken);
        if (_runtimeProcess.HasExited)
        {
            int exitCode = _runtimeProcess.ExitCode;
            string failure = string.IsNullOrWhiteSpace(_runtimeFailure) ? $"Runtime exited with code {exitCode}." : _runtimeFailure;
            DisposeRuntimeHandles();
            throw new InvalidOperationException(failure);
        }
        progress.Report("Better Movement for KBM is active.");
    }

    private static void LaunchGame(GameInstallation installation)
    {
        string uri = installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase)
            ? "steam://rungameid/460930"
            : "uplay://launch/1771/0";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static async Task WaitForGameAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            while (Process.GetProcessesByName("GRW").Length == 0) await Task.Delay(500, timeout.Token);
            await Task.Delay(1500, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Ghost Recon Wildlands did not start within four minutes.");
        }
    }

    private LauncherCheck BuildExecutableCheck(GameInstallation installation)
    {
        string executable = installation.Executable;
        FileInfo info = new(executable);
        string? hash = null;
        if (_hashedPath == executable && _hashedLength == info.Length && _hashedWriteTimeUtc == info.LastWriteTimeUtc)
            hash = _executableHash;
        else if (TryLoadHashCache(executable, info, out string? cachedHash))
        {
            hash = cachedHash;
            RememberHash(executable, info, cachedHash!);
        }
        else
        {
            Task<string> hashTask = StartHashTask(executable, info);
            if (!hashTask.IsCompleted)
                return new("executable", "Game executable", "Analyzing executable build…", CheckStatus.Warning, "Details", null,
                    "The executable is being identified in the background. Live game code will still be checked before enabling the mod.");
            if (hashTask.IsCompletedSuccessfully) hash = hashTask.Result;
            else
                return new("executable", "Game executable", "Build identification unavailable.", CheckStatus.Warning, "Review", "show-signatures",
                    "The executable hash could not be read. Live game code will still be checked before enabling the mod.");
        }

        string version = installation.BuildIdentifier ?? $"Ubisoft executable matches Steam build {TestedSteamBuild}";
        return hash!.Equals(TestedExecutableHash, StringComparison.OrdinalIgnoreCase)
            ? new("executable", "Game executable", "Tested build recognized.", CheckStatus.Ready, "Details", "show-signatures",
                $"{version}. The executable matches the tested SHA-256. Live game code is checked again before enabling the mod.")
            : new("executable", "Game executable", "Untested build detected.", CheckStatus.Warning, "Review", "show-signatures",
                $"{version}. The mod enables only if every live code check passes.");
    }

    private static LauncherCheck BuildSayNoToEacCheck(string gameDirectory) => SayNoToEacAppearsInstalled(gameDirectory)
        ? new("saynotoeac", "Anti-cheat configuration", "SayNoToEAC stub DLLs and original .BAK files detected.", CheckStatus.Ready, "Manage", "manage-eac")
        : new("saynotoeac", "Anti-cheat configuration", "Expected SayNoToEAC stub/backup layout was not detected.", CheckStatus.Blocked, "Manage", "manage-eac");

    private static LauncherCheck BuildFirewallCheck(string id, string title, FirewallStatus? status, string category)
    {
        status ??= new FirewallStatus(0, 0, 0);
        bool ready = status.Existing > 0 && status.Blocked >= status.Existing;
        string actionPrefix = category == "game" ? "grw" : "ubisoft";
        if (ready)
        {
            string ownership = status.Managed > 0 ? "Removable isolation rules are active." : "Compatible external block rules are active.";
            return new(id, title, $"{status.Blocked}/{status.Existing} detected executables blocked. {ownership}", CheckStatus.Ready,
                status.Managed > 0 ? (category == "ubisoft" ? "Uninstall" : "Remove") : null,
                status.Managed > 0 ? $"remove-{actionPrefix}-firewall" : null);
        }
        return new(id, title, $"{status.Blocked}/{status.Existing} detected executables blocked. This offline-isolation measure is recommended but optional.", CheckStatus.Warning, "Install", $"install-{actionPrefix}-firewall");
    }

    private static LauncherCheck BuildBackupCheck(GameInstallation? installation)
    {
        if (installation is null)
            return new("backup", "Save backup", "Select a game installation to identify its save data.", CheckStatus.Warning);
        SaveBackupSummary summary = SaveBackupService.GetSummary(installation);
        return new("backup", "Save backup", summary.Detail, summary.IsReady ? CheckStatus.Ready : CheckStatus.Warning,
            "Manage", "manage-saves", summary.ToolTip);
    }

    private void RefreshInstallationIfNeeded()
    {
        if (_installation is not null && DateTime.UtcNow - _installationCheckedAt < TimeSpan.FromSeconds(10) && File.Exists(_installation.Executable)) return;
        _installation = GameLocator.Find();
        _installationCheckedAt = DateTime.UtcNow;
    }

    private void RefreshFirewallIfNeeded()
    {
        if (DateTime.UtcNow - _firewallCheckedAt < TimeSpan.FromSeconds(10)) return;
        _gameFirewall = _installation is null ? new FirewallStatus(0, 0, 0) : FirewallService.Inspect(FirewallService.GamePrograms(_installation.Directory));
        _ubisoftFirewall = FirewallService.Inspect(FirewallService.UbisoftPrograms());
        _firewallCheckedAt = DateTime.UtcNow;
    }

    private async Task<string> GetExecutableHashAsync(string executable, CancellationToken cancellationToken)
    {
        FileInfo info = new(executable);
        if (_hashedPath == executable && _hashedLength == info.Length && _hashedWriteTimeUtc == info.LastWriteTimeUtc && _executableHash is not null) return _executableHash;
        if (TryLoadHashCache(executable, info, out string? cachedHash))
        {
            RememberHash(executable, info, cachedHash!);
            return cachedHash!;
        }
        return await StartHashTask(executable, info).WaitAsync(cancellationToken);
    }

    private Task<string> StartHashTask(string executable, FileInfo info)
    {
        if (_hashTask is not null && _hashTaskPath == executable && _hashTaskLength == info.Length && _hashTaskWriteTimeUtc == info.LastWriteTimeUtc)
            return _hashTask;
        _hashTaskPath = executable;
        _hashTaskLength = info.Length;
        _hashTaskWriteTimeUtc = info.LastWriteTimeUtc;
        _hashTask = Task.Run(() =>
        {
            using FileStream stream = new(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            RememberHash(executable, info, hash);
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllLines(HashCacheFile, [executable, info.Length.ToString(), info.LastWriteTimeUtc.Ticks.ToString(), hash]);
            return hash;
        });
        return _hashTask;
    }

    private static bool TryLoadHashCache(string executable, FileInfo info, out string? hash)
    {
        hash = null;
        try
        {
            string[] lines = File.ReadAllLines(HashCacheFile);
            if (lines.Length != 4 || !string.Equals(lines[0], executable, StringComparison.OrdinalIgnoreCase)) return false;
            if (!long.TryParse(lines[1], out long length) || length != info.Length) return false;
            if (!long.TryParse(lines[2], out long ticks) || ticks != info.LastWriteTimeUtc.Ticks) return false;
            if (lines[3].Length != 64) return false;
            hash = lines[3];
            return true;
        }
        catch { return false; }
    }

    private void RememberHash(string executable, FileInfo info, string hash)
    {
        _hashedPath = executable;
        _hashedLength = info.Length;
        _hashedWriteTimeUtc = info.LastWriteTimeUtc;
        _executableHash = hash;
    }

    private async Task<string> VerifyHooksAsync(CancellationToken cancellationToken)
    {
        if (Process.GetProcessesByName("GRW").Length == 0) return "Ghost Recon Wildlands is not running; exact hook instructions will be verified at attachment.";
        string runtime = ResolveRuntimeExecutable();
        ProcessStartInfo start = new(runtime, "--verify") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the verifier.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return "All exact original movement and ADS instructions were verified.";
    }

    private static bool SayNoToEacAppearsInstalled(string gameDirectory)
    {
        string eac = Path.Combine(gameDirectory, "EasyAntiCheat");
        string x64 = Path.Combine(eac, "EasyAntiCheat_x64.dll"), x64Backup = x64 + ".BAK";
        string x86 = Path.Combine(eac, "EasyAntiCheat_x86.dll"), x86Backup = x86 + ".BAK";
        return Small(x64) && Small(x86) && Large(x64Backup) && Large(x86Backup);
        static bool Small(string path) => File.Exists(path) && new FileInfo(path).Length is > 0 and < 65536;
        static bool Large(string path) => File.Exists(path) && new FileInfo(path).Length > 262144;
    }

    private static string[] ActiveEacProcesses() => Process.GetProcesses()
        .Where(process => process.ProcessName.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("eac", StringComparison.OrdinalIgnoreCase))
        .Select(process => process.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private void RunFirewall(string operation, string? gameDirectory = null)
    {
        int exitCode = FirewallService.RunElevated(operation, gameDirectory);
        if (exitCode != 0) throw new InvalidOperationException($"The elevated firewall helper failed with exit code {exitCode}.");
        _firewallCheckedAt = DateTime.MinValue;
    }

    private void EnsureInstallation()
    {
        RefreshInstallationIfNeeded();
        if (_installation is null) throw new DirectoryNotFoundException("A valid Ghost Recon Wildlands installation has not been selected.");
    }

    private static string ResolveRuntimeExecutable()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Runtime", "GRWAnalogueMovement.exe"),
            Path.Combine(AppContext.BaseDirectory, "GRWAnalogueMovement.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GRWMovementRuntime", "bin", "Release", "net8.0-windows", "GRWAnalogueMovement.exe"))
        ];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("The Better Movement for KBM runtime is missing. Reinstall the launcher or rebuild the solution.");
    }

    private void InvalidateCaches()
    {
        _installation = null;
        _installationCheckedAt = DateTime.MinValue;
        _firewallCheckedAt = DateTime.MinValue;
        _hashedPath = null;
        _executableHash = null;
        _hashTask = null;
        _hashTaskPath = null;
    }

    private void DisposeRuntimeHandles()
    {
        _runtimeProcess?.Dispose();
        _runtimeProcess = null;
        _shutdownEvent?.Dispose();
        _shutdownEvent = null;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public void Dispose()
    {
        _inspectionLock.Dispose();
        DisposeRuntimeHandles();
    }
}
