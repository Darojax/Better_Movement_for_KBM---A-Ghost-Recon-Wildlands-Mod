namespace GRWBetterMovementLauncher.Models;

public enum CheckStatus
{
    Ready,
    Inactive,
    Warning,
    Blocked
}

public sealed record LauncherCheck(
    string Id,
    string Title,
    string Detail,
    CheckStatus Status,
    string? ActionLabel = null,
    string? ActionId = null,
    string? DetailToolTip = null);

public sealed record LauncherSnapshot(
    string Storefront,
    string GamePath,
    IReadOnlyList<LauncherCheck> Checks,
    bool GameRunning,
    bool RuntimeActive,
    bool RuntimeDetected);

public sealed record LauncherActionResult(string Message, bool ShouldLog = true);
