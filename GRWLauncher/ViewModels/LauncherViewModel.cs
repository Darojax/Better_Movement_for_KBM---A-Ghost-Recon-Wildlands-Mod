using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GRWBetterMovementLauncher.Models;
using GRWBetterMovementLauncher.Services;

namespace GRWBetterMovementLauncher.ViewModels;

public sealed class LauncherViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILauncherBackend _backend;
    private bool _busy;
    private bool _refreshInProgress;
    private bool _runtimeActive;
    private bool _runtimeDetected;
    private bool _gameRunning;
    private bool _launchPending;
    private CancellationTokenSource? _launchCancellation;
    private string _headline = "Checking your installation…";
    private string _summary = "Please wait while the launcher evaluates the local environment.";
    private string _storefront = "Detecting…";
    private string _gamePath = "—";
    private string _activity = "Performing initial safety and compatibility checks…";
    private string _lastChecked = "Not checked yet";
    private Brush _summaryBrush = new SolidColorBrush(Color.FromRgb(183, 201, 109));

    public LauncherViewModel(ILauncherBackend backend)
    {
        _backend = backend;
        CheckActionCommand = new RelayCommand(item => _ = ExecuteCheckActionAsync((BindableCheck)item!), _ => !Busy);
        LaunchCommand = new RelayCommand(_ =>
        {
            if (LaunchPending) CancelLaunchAttempt();
            else if (RuntimeActive) _ = StopRuntimeAsync();
            else _ = StartWithModAsync(launchGame: !GameRunning);
        }, _ => LaunchPending || (RuntimeActive && !Busy) || (CanLaunch && !Busy));
        LaunchVanillaCommand = new RelayCommand(_ => _ = LaunchVanillaAsync(), _ => !Busy && !RuntimeActive && !GameRunning);
        ClearLogCommand = new RelayCommand(_ => ActivityLog.Clear());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BindableCheck> Checks { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];

    public ICommand CheckActionCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand LaunchVanillaCommand { get; }
    public ICommand ClearLogCommand { get; }

    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) { RefreshCommands(); OnPropertyChanged(nameof(LaunchToolTip)); OnPropertyChanged(nameof(LaunchVanillaToolTip)); } } }
    public bool RuntimeActive
    {
        get => _runtimeActive;
        private set
        {
            if (!Set(ref _runtimeActive, value)) return;
            RefreshCommands();
            OnPropertyChanged(nameof(PrimaryActionLabel));
            OnPropertyChanged(nameof(LaunchToolTip));
            OnPropertyChanged(nameof(LaunchVanillaToolTip));
        }
    }
    public bool RuntimeDetected { get => _runtimeDetected; private set => Set(ref _runtimeDetected, value); }
    public bool LaunchPending
    {
        get => _launchPending;
        private set
        {
            if (!Set(ref _launchPending, value)) return;
            RefreshCommands();
            OnPropertyChanged(nameof(PrimaryActionLabel));
            OnPropertyChanged(nameof(LaunchToolTip));
            OnPropertyChanged(nameof(LaunchVanillaToolTip));
        }
    }
    public bool GameRunning
    {
        get => _gameRunning;
        private set
        {
            if (!Set(ref _gameRunning, value)) return;
            RefreshCommands();
            OnPropertyChanged(nameof(PrimaryActionLabel));
            OnPropertyChanged(nameof(LaunchToolTip));
            OnPropertyChanged(nameof(LaunchVanillaToolTip));
        }
    }
    public bool CanLaunch => Checks.Count > 0 && Checks.All(item => item.Status != CheckStatus.Blocked);
    public string PrimaryActionLabel => LaunchPending
        ? "Cancel launch attempt"
        : RuntimeActive
        ? "Disable Better Movement for KBM"
        : GameRunning
        ? "Enable Better Movement for KBM"
        : "Launch with Better Movement for KBM";
    public string? LaunchToolTip
    {
        get
        {
            if (LaunchPending) return "Stops waiting for Ghost Recon Wildlands and restores all launcher controls";
            if (RuntimeActive) return "Disables the mod while Ghost Recon Wildlands remains running";
            if (Checks.Any(item => item.Status == CheckStatus.Blocked)) return "Mod cannot run due to a blocked item";
            if (Busy) return "Please wait for the current launcher action to finish";
            if (Checks.Count == 0) return "Safety and compatibility checks are still running";
            return GameRunning
                ? "Enables the mod in the running game"
                : "Launches the game together with the mod";
        }
    }
    public string LaunchVanillaToolTip => GameRunning
        ? "Ghost Recon Wildlands is already running"
        : RuntimeActive
            ? "Disable Better Movement for KBM before starting another game session"
            : Busy
                ? "Please wait for the current launcher action to finish"
                : "Launches the game normally without introducing the mod";
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Storefront { get => _storefront; private set => Set(ref _storefront, value); }
    public string GamePath { get => _gamePath; private set => Set(ref _gamePath, value); }
    public string Activity { get => _activity; private set => Set(ref _activity, value); }
    public string LastChecked { get => _lastChecked; private set => Set(ref _lastChecked, value); }
    public Brush SummaryBrush { get => _summaryBrush; private set => Set(ref _summaryBrush, value); }

    public async Task InitializeAsync() => await RefreshAsync();

    public async Task RefreshAfterSaveBackupAsync()
    {
        _backend.InvalidateBackupSummary();
        while (_refreshInProgress)
            await Task.Delay(25);
        await RefreshAsync(recordInLog: false);
    }

    public async Task RefreshAsync(bool recordInLog = true)
    {
        if (_refreshInProgress || (recordInLog && Busy)) return;
        _refreshInProgress = true;
        if (recordInLog)
        {
            Busy = true;
            Activity = "Refreshing safety and compatibility checks…";
        }
        try
        {
            LauncherSnapshot snapshot = await _backend.InspectAsync();
            bool runtimeWasActive = RuntimeActive;
            ApplyChecks(snapshot.Checks);
            Storefront = snapshot.Storefront;
            GamePath = snapshot.GamePath;
            GameRunning = snapshot.GameRunning;
            RuntimeActive = snapshot.RuntimeActive;
            RuntimeDetected = snapshot.RuntimeDetected;
            if (runtimeWasActive && !RuntimeActive)
            {
                Activity = snapshot.GameRunning ? "The movement runtime stopped; Ghost Recon Wildlands remains open." : "Ghost Recon Wildlands and the movement runtime have stopped.";
                AddLog("Movement runtime stopped and its restoration path completed.");
            }
            LastChecked = DateTime.Now.ToString("HH:mm:ss");
            UpdateSummary();
        }
        catch (Exception exception)
        {
            Headline = "The inspection could not be completed";
            Summary = exception.Message;
            AddLog($"Error: {exception.Message}");
        }
        finally
        {
            _refreshInProgress = false;
            if (recordInLog)
            {
                Busy = false;
                Activity = RuntimeActive ? "Better Movement for KBM is active." : GameRunning ? "Ghost Recon Wildlands is running without the mod." : "Ready.";
            }
            OnPropertyChanged(nameof(CanLaunch));
            OnPropertyChanged(nameof(LaunchToolTip));
        }
    }

    private async Task ExecuteCheckActionAsync(BindableCheck item)
    {
        if (Busy || string.IsNullOrWhiteSpace(item.ActionId)) return;
        Busy = true;
        Activity = $"Working on {item.Title.ToLowerInvariant()}…";
        try
        {
            LauncherActionResult result = await _backend.ExecuteAsync(item.ActionId);
            if (result.ShouldLog) AddLog(result.Message);
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Error: {exception.Message}");
        }
        finally
        {
            Busy = false;
            await RefreshAsync();
        }
    }

    private async Task StartWithModAsync(bool launchGame)
    {
        if (!CanLaunch || Busy) return;
        if (!ConfirmRiskIfRequired()) return;
        using CancellationTokenSource cancellation = new();
        _launchCancellation = cancellation;
        LaunchPending = true;
        Busy = true;
        if (!launchGame) AddLog("Enabling Better Movement for KBM in the running game.");
        try
        {
            Progress<string> progress = CreateProgress();
            if (launchGame) await _backend.LaunchWithModAsync(progress, cancellation.Token);
            else await _backend.AttachAsync(progress, cancellation.Token);
            RuntimeActive = true;
            RuntimeDetected = true;
            Headline = "Better Movement for KBM is active";
            Summary = "The launcher is supervising the runtime and will restore the original in-memory instructions when the mod stops.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Activity = "Launch attempt cancelled.";
            AddLog("Launch attempt cancelled by the user.");
            await RefreshAsync(recordInLog: false);
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Unable to enable mod: {exception.Message}");
            await RefreshAsync(recordInLog: false);
        }
        finally
        {
            if (ReferenceEquals(_launchCancellation, cancellation)) _launchCancellation = null;
            LaunchPending = false;
            Busy = false;
        }
    }

    private void CancelLaunchAttempt()
    {
        if (!LaunchPending || _launchCancellation is null || _launchCancellation.IsCancellationRequested) return;
        Activity = "Cancelling launch attempt…";
        _launchCancellation.Cancel();
    }

    private async Task LaunchVanillaAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            await _backend.LaunchVanillaAsync(CreateProgress());
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Unable to launch Ghost Recon Wildlands: {exception.Message}");
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task StopRuntimeAsync()
    {
        if (!RuntimeActive || Busy) return;
        Busy = true;
        try
        {
            await _backend.StopRuntimeAsync(CreateProgress());
            RuntimeActive = false;
            RuntimeDetected = false;
            Activity = "Better Movement for KBM is disabled; Ghost Recon Wildlands remains open.";
            AddLog("Original in-memory instructions restored; runtime stopped.");
            await RefreshAsync(recordInLog: false);
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Unable to disable mod cleanly: {exception.Message}");
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task<bool> RemoveLauncherDataAsync()
    {
        if (RuntimeDetected || Busy) return false;
        Busy = true;
        try
        {
            LauncherActionResult result = await _backend.ExecuteAsync("remove-launcher-data");
            Activity = result.Message;
            return true;
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Cleanup failed: {exception.Message}");
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    private bool ConfirmRiskIfRequired()
    {
        if (!_backend.RiskAcceptanceRequired) return true;
        MessageBoxResult result = MessageBox.Show(
            "Better Movement for KBM is an unofficial single-player mod that writes to the Ghost Recon Wildlands process. Use it offline only. No configuration can guarantee protection from sanctions, crashes, save loss, or other damage.\n\nDo you understand and want to continue?",
            "Offline single-player risk acknowledgement",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return false;
        _backend.RecordRiskAcceptance();
        AddLog("Offline single-player risk acknowledgement recorded.");
        return true;
    }

    private Progress<string> CreateProgress() => new(stage =>
    {
        Activity = stage;
        AddLog(stage);
    });

    private void UpdateSummary()
    {
        if (RuntimeActive)
        {
            SummaryBrush = new SolidColorBrush(Color.FromRgb(101, 201, 135));
            Headline = "Better Movement for KBM is active";
            Summary = "The runtime is attached and active.";
            return;
        }

        int blocked = Checks.Count(item => item.Status == CheckStatus.Blocked);
        int warnings = Checks.Count(item => item.Status == CheckStatus.Warning);
        if (blocked > 0)
        {
            SummaryBrush = new SolidColorBrush(Color.FromRgb(228, 111, 103));
            Headline = "Action required before the mod can start";
            Summary = $"{blocked} blocking condition{(blocked == 1 ? "" : "s")} detected. The game can still be launched without the mod.";
        }
        else if (warnings > 0)
        {
            SummaryBrush = new SolidColorBrush(Color.FromRgb(230, 184, 92));
            Headline = "Ready to launch with cautions";
            Summary = $"No blocking problems detected. {warnings} caution item{(warnings == 1 ? "" : "s")} can be reviewed or left unchanged.";
        }
        else
        {
            SummaryBrush = new SolidColorBrush(Color.FromRgb(101, 201, 135));
            Headline = "Ready to launch";
            Summary = "Every safety and compatibility check passed.";
        }
    }

    private void AddLog(string message)
    {
        ActivityLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (ActivityLog.Count > 40) ActivityLog.RemoveAt(0);
    }

    private void ApplyChecks(IReadOnlyList<LauncherCheck> incoming)
    {
        bool sameRows = Checks.Count == incoming.Count
            && Checks.Select(item => item.Id).SequenceEqual(incoming.Select(item => item.Id));
        if (!sameRows)
        {
            Checks.Clear();
            foreach (LauncherCheck check in incoming) Checks.Add(new BindableCheck(check));
            return;
        }

        for (int index = 0; index < incoming.Count; index++)
            Checks[index].UpdateFrom(incoming[index]);
    }

    private void RefreshCommands()
    {
        foreach (RelayCommand command in new[] { CheckActionCommand, LaunchCommand, LaunchVanillaCommand }.OfType<RelayCommand>())
            command.RaiseCanExecuteChanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _launchCancellation?.Cancel();
        (_backend as IDisposable)?.Dispose();
    }
}
