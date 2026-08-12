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
    private bool _gameRunning;
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
        LaunchCommand = new RelayCommand(_ => _ = StartWithModAsync(launchGame: true), _ => CanLaunch && !Busy && !RuntimeActive);
        LaunchVanillaCommand = new RelayCommand(_ => _ = LaunchVanillaAsync(), _ => !Busy && !RuntimeActive);
        AttachCommand = new RelayCommand(_ => _ = StartWithModAsync(launchGame: false), _ => CanLaunch && GameRunning && !Busy && !RuntimeActive);
        RestoreCommand = new RelayCommand(_ => _ = StopRuntimeAsync(), _ => RuntimeActive && !Busy);
        ClearLogCommand = new RelayCommand(_ => ActivityLog.Clear());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BindableCheck> Checks { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];

    public ICommand CheckActionCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand LaunchVanillaCommand { get; }
    public ICommand AttachCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ClearLogCommand { get; }

    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) RefreshCommands(); } }
    public bool RuntimeActive { get => _runtimeActive; private set { if (Set(ref _runtimeActive, value)) RefreshCommands(); } }
    public bool GameRunning { get => _gameRunning; private set { if (Set(ref _gameRunning, value)) RefreshCommands(); } }
    public bool CanLaunch => Checks.Count > 0 && Checks.All(item => item.Status != CheckStatus.Blocked);
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Storefront { get => _storefront; private set => Set(ref _storefront, value); }
    public string GamePath { get => _gamePath; private set => Set(ref _gamePath, value); }
    public string Activity { get => _activity; private set => Set(ref _activity, value); }
    public string LastChecked { get => _lastChecked; private set => Set(ref _lastChecked, value); }
    public Brush SummaryBrush { get => _summaryBrush; private set => Set(ref _summaryBrush, value); }

    public async Task InitializeAsync() => await RefreshAsync();

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
        Busy = true;
        if (!launchGame) AddLog("Enable in running game requested.");
        try
        {
            Progress<string> progress = CreateProgress();
            if (launchGame) await _backend.LaunchWithModAsync(progress);
            else await _backend.AttachAsync(progress);
            RuntimeActive = true;
            Headline = "Better Movement for KBM is active";
            Summary = "The launcher is supervising the runtime and will restore the original in-memory instructions when the mod stops.";
        }
        catch (Exception exception)
        {
            Activity = exception.Message;
            AddLog($"Unable to enable mod: {exception.Message}");
            await RefreshAsync(recordInLog: false);
        }
        finally
        {
            Busy = false;
        }
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
            Summary = "The runtime is attached and supervised. Disable the mod or close the launcher to restore the original instructions.";
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
        foreach (RelayCommand command in new[] { CheckActionCommand, LaunchCommand, LaunchVanillaCommand, AttachCommand, RestoreCommand }.OfType<RelayCommand>())
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

    public void Dispose() => (_backend as IDisposable)?.Dispose();
}
