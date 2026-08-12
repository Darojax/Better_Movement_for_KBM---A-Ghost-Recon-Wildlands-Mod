using GRWBetterMovementLauncher.Models;

namespace GRWBetterMovementLauncher.Services;

public interface ILauncherBackend
{
    bool RiskAcceptanceRequired { get; }

    Task<LauncherSnapshot> InspectAsync(CancellationToken cancellationToken = default);
    Task<LauncherActionResult> ExecuteAsync(string actionId, CancellationToken cancellationToken = default);
    Task LaunchWithModAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
    Task LaunchVanillaAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
    Task AttachAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
    Task StopRuntimeAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
    void InvalidateBackupSummary();
    void RecordRiskAcceptance();
}
