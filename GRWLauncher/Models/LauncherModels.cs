namespace GRWBetterMovementLauncher.Models;

public enum CheckStatus
{
    Ready,
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
    bool RuntimeActive);

public sealed record LauncherActionResult(string Message, bool ShouldLog = true);
