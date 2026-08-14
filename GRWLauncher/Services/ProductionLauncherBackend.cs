using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GRWBetterMovementLauncher.Models;

namespace GRWBetterMovementLauncher.Services;

public sealed class ProductionLauncherBackend : ILauncherBackend, IDisposable
{
    private const string LegacyExecutableHash = "56791FF5A6C213A77EEBEDAEAEE3026D63B70806071358CE96ABD3ED7947ADE7";
    private const string CurrentExecutableHash = "4B222677C5068D40104144AF79F0E31FDC4D62D1A48F6BA07BC70B4EE167E56E";
    private const string LegacySteamBuild = "24446260";
    private const string CurrentSteamBuild = "24669148";
    private const string CurrentGameVersion = "133.1.0.9840374";
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
    private volatile bool _runtimeStarting;
    private SaveBackupSummary? _backupSummary;
    private string? _backupSummaryInstallation;
    private DateTime _backupSummaryCheckedAt;
    private bool? _lastObservedGameRunning;
    private DateTime? _gameStoppedAtUtc;

    public bool RiskAcceptanceRequired => !File.Exists(AcceptanceFile);

    public async Task<LauncherSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        await _inspectionLock.WaitAsync(cancellationToken);
        try
        {
            RefreshInstallationIfNeeded();
            ProcessState processes = CaptureProcessState();
            bool gameRunning = processes.GameRunning;
            TrackGameRunningState(gameRunning);
            if (!_runtimeStarting && _runtimeProcess is { HasExited: true }) DisposeRuntimeHandles();
            bool runtimeActive = _runtimeProcess is { HasExited: false };
            bool foreignRuntime = processes.RuntimeProcessIds.Any(id => _runtimeProcess is null || id != _runtimeProcess.Id);

            List<LauncherCheck> checks = [];
            if (_installation is null)
            {
                checks.Add(new("installation", "Game installation", "Ghost Recon Wildlands was not detected. Select the folder containing GRW.exe.", CheckStatus.Blocked, "Choose", "choose-game"));
                checks.Add(new("executable", "Game executable", "Cannot verify compatibility until the installation is selected.", CheckStatus.Blocked));
                checks.Add(new("saynotoeac", "SayNoToEAC", "Cannot inspect SayNoToEAC until the installation is selected.", CheckStatus.Blocked, "Manage", "manage-eac"));
            }
            else
            {
                checks.Add(new("installation", "Game installation", $"{_installation.Storefront} · {_installation.Directory}", CheckStatus.Ready, "Change", "choose-game"));
                checks.Add(BuildExecutableCheck(_installation));
                checks.Add(BuildSayNoToEacCheck(_installation));
            }

            string[] eacProcesses = processes.EacProcesses;
            bool nativeEacFreeBuild = _installation is not null && IsCurrentEacFreeBuild(_installation.Executable);
            checks.Add(eacProcesses.Length == 0
                ? nativeEacFreeBuild
                    ? new("eac", "Easy Anti-Cheat", $"Not included in compatible game version {CurrentGameVersion}.", CheckStatus.Ready)
                    : gameRunning
                        ? new("eac", "Easy Anti-Cheat", "No active Easy Anti-Cheat process detected.", CheckStatus.Ready)
                        : new("eac", "Easy Anti-Cheat", "Not detected, but Ghost Recon Wildlands is not running.", CheckStatus.Inactive)
                : new("eac", "Easy Anti-Cheat", $"Active process detected: {string.Join(", ", eacProcesses)}.", CheckStatus.Blocked));

            RefreshFirewallIfNeeded();
            checks.Add(BuildFirewallCheck("grw-firewall", "Ghost Recon Wildlands Windows Firewall block", _gameFirewall, "game"));
            checks.Add(BuildFirewallCheck("ubisoft-firewall", "Ubisoft Connect Windows Firewall block", _ubisoftFirewall, "ubisoft"));

            checks.Add(foreignRuntime
                ? new("hooks", "Mod runtime hook", "Another movement runtime is already running. Close Ghost Recon Wildlands before starting a new session.", CheckStatus.Blocked)
                : runtimeActive
                    ? new("hooks", "Mod runtime hook", "Attached and Active.", CheckStatus.Ready)
                    : gameRunning
                        ? new("hooks", "Mod runtime hook", "Ghost Recon Wildlands is running without the mod.", CheckStatus.Inactive, "Verify", "verify-hooks")
                        : new("hooks", "Mod runtime hook", "Ghost Recon Wildlands is not running.", CheckStatus.Inactive));

            checks.Add(BuildBackupCheck(_installation, gameRunning));
            string storefront = _installation?.Storefront ?? "Not detected";
            string path = _installation?.Directory ?? "Choose the folder containing GRW.exe";
            return new LauncherSnapshot(storefront, path, checks, gameRunning, runtimeActive, runtimeActive || foreignRuntime);
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
                RunFirewall(actionId == "install-grw-firewall" ? "install-game" : "remove-game-blocks", _installation!.Directory);
                return new LauncherActionResult(actionId.StartsWith("install", StringComparison.Ordinal) ? "Ghost Recon Wildlands Windows Firewall block installed." : "Detected Ghost Recon Wildlands Windows Firewall block rules uninstalled.");

            case "install-ubisoft-firewall":
            case "remove-ubisoft-firewall":
                RunFirewall(actionId == "install-ubisoft-firewall" ? "install-ubisoft" : "remove-ubisoft-blocks");
                return new LauncherActionResult(actionId.StartsWith("install", StringComparison.Ordinal) ? "Ubisoft Connect Windows Firewall block installed." : "Detected Ubisoft Connect Windows Firewall block rules uninstalled.");

            case "manage-saves":
                EnsureInstallation();
                if (System.Windows.Application.Current.MainWindow is not MainWindow saveWindow)
                    throw new InvalidOperationException("The launcher window is unavailable.");
                await saveWindow.ShowSaveManagementAsync(_installation!);
                return new LauncherActionResult("Save backup management panel closed.", ShouldLog: false);

            case "remove-launcher-data":
                if (_runtimeProcess is { HasExited: false } || Process.GetProcessesByName("GRWAnalogueMovement").Length > 0)
                    throw new InvalidOperationException("Disable Better Movement for KBM before removing launcher data.");
                await Task.Run(LauncherCleanupService.RemoveLauncherData, cancellationToken);
                return new LauncherActionResult("Launcher-created backups, local data, and managed firewall rules were removed.");

            case "manage-eac":
                EnsureInstallation();
                if (System.Windows.Application.Current.MainWindow is not MainWindow mainWindow)
                    throw new InvalidOperationException("The launcher window is unavailable.");
                await mainWindow.ShowAntiCheatManagementAsync(_installation!.Directory);
                return new LauncherActionResult("SayNoToEAC management panel closed.", ShouldLog: false);

            case "show-signatures":
                EnsureInstallation();
                string hash = await GetExecutableHashAsync(_installation!.Executable, cancellationToken);
                return new LauncherActionResult(TryGetCompatibleBuild(hash, out string? build)
                    ? $"{_installation.Storefront} executable matches the tested Steam public build {build}. SHA-256: {hash}"
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
        bool launchedGame = false;
        DateTime startupDeadline = DateTime.UtcNow.AddMinutes(4);
        if (Process.GetProcessesByName("GRW").Length == 0)
        {
            progress.Report($"Starting Ghost Recon Wildlands through {_installation!.Storefront}…");
            LaunchGame(_installation);
            launchedGame = true;
        }

        while (true)
        {
            int? gameProcessId;
            if (launchedGame)
            {
                progress.Report("Waiting for a stable GRW.exe process…");
                gameProcessId = await WaitForStableGameAsync(_installation!.Executable, startupDeadline, cancellationToken);
            }
            else gameProcessId = FindSelectedGameProcessId(_installation!.Executable);
            try
            {
                if (gameProcessId is null)
                    throw new InvalidOperationException("The selected Ghost Recon Wildlands installation is not running.");
                await StartRuntimeAsync(progress, gameProcessId.Value, cancellationToken);
                return;
            }
            catch (RuntimeStartupInterruptedException) when (launchedGame && DateTime.UtcNow < startupDeadline)
            {
                progress.Report("Ghost Recon Wildlands startup was interrupted by the launcher. Complete any Ubisoft Connect prompt; the mod will keep waiting…");
            }
        }
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
        EnsureInstallation();
        int? gameProcessId = FindSelectedGameProcessId(_installation!.Executable);
        if (gameProcessId is null) throw new InvalidOperationException("The selected Ghost Recon Wildlands installation is not running.");
        await StartRuntimeAsync(progress, gameProcessId.Value, cancellationToken);
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
            throw new TimeoutException("The runtime did not confirm restoration. Close Ghost Recon Wildlands before continuing.");
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

    private async Task StartRuntimeAsync(IProgress<string> progress, int gameProcessId, CancellationToken cancellationToken)
    {
        if (_runtimeProcess is { HasExited: false }) throw new InvalidOperationException("Better Movement for KBM is already active.");
        if (ActiveEacProcesses().Length > 0) throw new InvalidOperationException("Easy Anti-Cheat is active. The mod will not attach.");
        EnsureInstallation();
        string executableHash = await GetExecutableHashAsync(_installation!.Executable, cancellationToken);
        if (!executableHash.Equals(CurrentExecutableHash, StringComparison.OrdinalIgnoreCase)
            && !SayNoToEacAppearsInstalled(_installation.Directory))
            throw new InvalidOperationException("SayNoToEAC was not detected. The mod will not attach.");
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
            Arguments = $"--pid={gameProcessId} --shutdown-event={Quote(eventName)}"
        };
        Process runtimeProcess = new() { StartInfo = start, EnableRaisingEvents = true };
        bool runtimeStarted = false;
        _runtimeProcess = runtimeProcess;
        _runtimeStarting = true;
        try
        {
            runtimeProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.Contains("Verified movement", StringComparison.OrdinalIgnoreCase)) progress.Report("Movement runtime attached successfully."); };
            runtimeProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _runtimeFailure = string.IsNullOrWhiteSpace(_runtimeFailure) ? e.Data : $"{_runtimeFailure}{Environment.NewLine}{e.Data}";
            };
            if (!runtimeProcess.Start()) throw new InvalidOperationException("Could not start the movement runtime.");
            runtimeStarted = true;
            runtimeProcess.BeginOutputReadLine();
            runtimeProcess.BeginErrorReadLine();
            await Task.Delay(3000, cancellationToken);
            if (runtimeProcess.HasExited)
            {
                int exitCode = runtimeProcess.ExitCode;
                string failure = string.IsNullOrWhiteSpace(_runtimeFailure)
                    ? $"Runtime exited with code {exitCode}."
                    : _runtimeFailure.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => !line.TrimStart().StartsWith("at ", StringComparison.Ordinal)) ?? _runtimeFailure;
                DisposeRuntimeHandles();
                if (exitCode == 0) throw new RuntimeStartupInterruptedException();
                throw new InvalidOperationException(failure);
            }
            progress.Report("Better Movement for KBM is active.");
        }
        catch (OperationCanceledException)
        {
            if (runtimeStarted && !runtimeProcess.HasExited)
            {
                _shutdownEvent?.Set();
                using CancellationTokenSource restorationTimeout = new(TimeSpan.FromSeconds(8));
                try { await runtimeProcess.WaitForExitAsync(restorationTimeout.Token); }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The cancelled runtime did not confirm restoration. Exit Ghost Recon Wildlands before continuing.");
                }
            }
            if (ReferenceEquals(_runtimeProcess, runtimeProcess)) DisposeRuntimeHandles();
            throw;
        }
        catch
        {
            if (ReferenceEquals(_runtimeProcess, runtimeProcess) && (!runtimeStarted || runtimeProcess.HasExited))
                DisposeRuntimeHandles();
            throw;
        }
        finally
        {
            _runtimeStarting = false;
        }
    }

    private static void LaunchGame(GameInstallation installation)
    {
        string uri = installation.Storefront.Equals("Steam", StringComparison.OrdinalIgnoreCase)
            ? "steam://rungameid/460930"
            : "uplay://launch/1771/0";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static async Task<int> WaitForStableGameAsync(string selectedExecutable, DateTime deadlineUtc, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Ghost Recon Wildlands did not start within four minutes.");
        timeout.CancelAfter(remaining);
        try
        {
            DateTime? continuouslyRunningSince = null;
            int? stableProcessId = null;
            while (true)
            {
                int? processId = FindSelectedGameProcessId(selectedExecutable);
                if (processId != stableProcessId)
                {
                    stableProcessId = processId;
                    continuouslyRunningSince = processId is null ? null : DateTime.UtcNow;
                }
                if (stableProcessId is not null && continuouslyRunningSince is not null &&
                    DateTime.UtcNow - continuouslyRunningSince >= TimeSpan.FromSeconds(4)) return stableProcessId.Value;
                await Task.Delay(500, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Ghost Recon Wildlands did not start within four minutes.");
        }
    }

    private static int? FindSelectedGameProcessId(string selectedExecutable)
    {
        string expectedPath = Path.GetFullPath(selectedExecutable);
        List<(int Id, DateTime Started)> matches = [];
        List<(int Id, DateTime Started)> all = [];
        foreach (Process process in Process.GetProcessesByName("GRW"))
        {
            using (process)
            {
                try
                {
                    DateTime started = process.StartTime;
                    all.Add((process.Id, started));
                    string? actualPath = process.MainModule?.FileName;
                    if (actualPath is not null && Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                        matches.Add((process.Id, started));
                }
                catch { }
            }
        }

        if (matches.Count > 0) return matches.MaxBy(candidate => candidate.Started).Id;
        return all.Count == 1 ? all[0].Id : null;
    }

    private sealed class RuntimeStartupInterruptedException : Exception { }

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
                return new("executable", "Game executable", "Analyzing executable build…", CheckStatus.Warning, null, null,
                    "The executable is being identified in the background. Live game code will still be checked before enabling the mod.");
            if (hashTask.IsCompletedSuccessfully) hash = hashTask.Result;
            else
                return new("executable", "Game executable", "Build identification unavailable.", CheckStatus.Warning, null, null,
                    "The executable hash could not be read. Live game code will still be checked before enabling the mod.");
        }

        bool compatible = TryGetCompatibleBuild(hash!, out string? compatibleBuild);
        string version = installation.BuildIdentifier ?? (compatible
            ? $"Ubisoft executable matches Steam build {compatibleBuild}"
            : "Ubisoft Connect build identifier unavailable");
        return compatible
            ? new("executable", "Game executable", compatibleBuild == CurrentSteamBuild
                    ? $"Game version {CurrentGameVersion} is compatible."
                    : $"Game executable matches the compatible Steam build {compatibleBuild}.", CheckStatus.Ready, null, null,
                $"{version}. The executable matches the tested SHA-256. Live game code is checked again before enabling the mod.")
            : new("executable", "Game executable", "Untested build detected.", CheckStatus.Warning, null, null,
                $"{version}. The mod enables only if every live code check passes.");
    }

    private LauncherCheck BuildSayNoToEacCheck(GameInstallation installation)
    {
        if (IsCurrentEacFreeBuild(installation.Executable))
            return new("saynotoeac", "SayNoToEAC", $"Not required for game version {CurrentGameVersion}.", CheckStatus.Ready);

        if (SayNoToEacAppearsInstalled(installation.Directory))
            return new("saynotoeac", "SayNoToEAC", "SayNoToEAC replacement DLLs detected.", CheckStatus.Ready, "Manage", "manage-eac");

        return new("saynotoeac", "SayNoToEAC", "SayNoToEAC was not detected in this game installation.", CheckStatus.Blocked, "Manage", "manage-eac");
    }

    private string? CurrentExecutableHashFor(string executable) =>
        string.Equals(_hashedPath, executable, StringComparison.OrdinalIgnoreCase) ? _executableHash : null;

    private bool IsCurrentEacFreeBuild(string executable) =>
        CurrentExecutableHash.Equals(CurrentExecutableHashFor(executable), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetCompatibleBuild(string hash, out string? build)
    {
        if (hash.Equals(CurrentExecutableHash, StringComparison.OrdinalIgnoreCase))
        {
            build = CurrentSteamBuild;
            return true;
        }
        if (hash.Equals(LegacyExecutableHash, StringComparison.OrdinalIgnoreCase))
        {
            build = LegacySteamBuild;
            return true;
        }
        build = null;
        return false;
    }

    private static LauncherCheck BuildFirewallCheck(string id, string title, FirewallStatus? status, string category)
    {
        status ??= new FirewallStatus(0, 0, 0);
        bool ready = status.Existing > 0 && status.Blocked >= status.Existing;
        string actionPrefix = category == "game" ? "grw" : "ubisoft";
        if (ready)
        {
            string ownership = status.Managed > 0 ? "Removable Windows Firewall rules are active." : "Compatible external block rules are active.";
            return new(id, title, $"{status.Blocked}/{status.Existing} detected executables blocked. {ownership}", CheckStatus.Ready,
                "Uninstall", $"remove-{actionPrefix}-firewall");
        }
        return new(id, title, $"{status.Blocked}/{status.Existing} detected executables blocked. This outbound firewall block is recommended but optional.", CheckStatus.Warning, "Install", $"install-{actionPrefix}-firewall");
    }

    private LauncherCheck BuildBackupCheck(GameInstallation? installation, bool gameRunning)
    {
        if (installation is null)
            return new("backup", "Save backup", "Select a game installation to identify its save data.", CheckStatus.Warning);

        if (gameRunning)
        {
            return new("backup", "Save backup", "Backup status will be checked when Ghost Recon Wildlands closes.", CheckStatus.Inactive,
                "Manage", "manage-saves", "Backups cannot be created while Ghost Recon Wildlands is running.");
        }

        if (_gameStoppedAtUtc is DateTime stoppedAt && DateTime.UtcNow - stoppedAt < TimeSpan.FromSeconds(2))
        {
            return new("backup", "Save backup", "Waiting for final save writes before checking backup status.", CheckStatus.Inactive,
                "Manage", "manage-saves");
        }

        _gameStoppedAtUtc = null;
        string installationKey = $"{installation.Storefront}|{installation.Directory}";
        if (_backupSummary is null || !_backupSummaryInstallation!.Equals(installationKey, StringComparison.OrdinalIgnoreCase)
            || DateTime.UtcNow - _backupSummaryCheckedAt >= TimeSpan.FromSeconds(5))
        {
            _backupSummary = SaveBackupService.GetSummary(installation);
            _backupSummaryInstallation = installationKey;
            _backupSummaryCheckedAt = DateTime.UtcNow;
        }
        SaveBackupSummary summary = _backupSummary;
        return new("backup", "Save backup", summary.Detail, summary.IsReady ? CheckStatus.Ready : CheckStatus.Warning,
            "Manage", "manage-saves", summary.ToolTip);
    }

    private void TrackGameRunningState(bool gameRunning)
    {
        if (_lastObservedGameRunning == gameRunning) return;

        if (_lastObservedGameRunning == true && !gameRunning)
        {
            _gameStoppedAtUtc = DateTime.UtcNow;
            InvalidateBackupSummary();
        }
        else if (gameRunning)
        {
            _gameStoppedAtUtc = null;
        }

        _lastObservedGameRunning = gameRunning;
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
        EnsureInstallation();
        int? gameProcessId = FindSelectedGameProcessId(_installation!.Executable);
        if (gameProcessId is null) return "Ghost Recon Wildlands is not running; exact hook instructions will be verified at attachment.";
        string runtime = ResolveRuntimeExecutable();
        ProcessStartInfo start = new(runtime, $"--pid={gameProcessId.Value} --verify") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the verifier.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return "All exact original movement and ADS instructions were verified.";
    }

    internal static bool SayNoToEacAppearsInstalled(string gameDirectory)
    {
        string eac = Path.Combine(gameDirectory, "EasyAntiCheat");
        string x64 = Path.Combine(eac, "EasyAntiCheat_x64.dll");
        string x86 = Path.Combine(eac, "EasyAntiCheat_x86.dll");
        return Small(x64) && Small(x86);
        static bool Small(string path) => File.Exists(path) && new FileInfo(path).Length is > 0 and < 65536;
    }

    private static string[] ActiveEacProcesses() => CaptureProcessState().EacProcesses;

    private static ProcessState CaptureProcessState()
    {
        bool gameRunning = false;
        List<int> runtimeIds = [];
        HashSet<string> eacProcesses = new(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; }
                catch { continue; }
                if (name.Equals("GRW", StringComparison.OrdinalIgnoreCase)) gameRunning = true;
                if (name.Equals("GRWAnalogueMovement", StringComparison.OrdinalIgnoreCase)) runtimeIds.Add(process.Id);
                if (name.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase) || name.Equals("eac", StringComparison.OrdinalIgnoreCase))
                    eacProcesses.Add(name);
            }
        }
        return new ProcessState(gameRunning, runtimeIds.ToArray(), eacProcesses.ToArray());
    }

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
        InvalidateBackupSummary();
    }

    public void InvalidateBackupSummary()
    {
        _backupSummary = null;
        _backupSummaryInstallation = null;
        _backupSummaryCheckedAt = DateTime.MinValue;
    }

    private void DisposeRuntimeHandles()
    {
        _runtimeProcess?.Dispose();
        _runtimeProcess = null;
        _shutdownEvent?.Dispose();
        _shutdownEvent = null;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed record ProcessState(bool GameRunning, int[] RuntimeProcessIds, string[] EacProcesses);

    public void Dispose()
    {
        _inspectionLock.Dispose();
        DisposeRuntimeHandles();
    }
}
