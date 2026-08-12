using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GRWBetterMovementLauncher.Models;

namespace GRWBetterMovementLauncher.ViewModels;

public sealed class BindableCheck : INotifyPropertyChanged
{
    private string _title = "";
    private string _detail = "";
    private string _detailToolTip = "";

    public BindableCheck(LauncherCheck source)
    {
        Id = source.Id;
        UpdateFrom(source);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string Title => _title;
    public string Detail => _detail;
    public string DetailToolTip => _detailToolTip;
    public CheckStatus Status { get; private set; }
    public string? ActionLabel { get; private set; }
    public string? ActionId { get; private set; }
    public bool IsAnalyzing { get; private set; }
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionLabel);
    public bool CanExecuteAction => !string.IsNullOrWhiteSpace(ActionId);
    public string StatusGlyph => Status switch
    {
        CheckStatus.Ready => "✓",
        CheckStatus.Inactive => "–",
        CheckStatus.Warning => "!",
        _ => "×"
    };
    public string StatusLabel => IsAnalyzing ? "Analyzing" : Id == "hooks" && Status == CheckStatus.Inactive &&
        Detail.StartsWith("Ghost Recon Wildlands is running", StringComparison.Ordinal)
        ? "Mod not running" : Status switch
    {
        CheckStatus.Ready => "Ready",
        CheckStatus.Inactive => "Game not running",
        CheckStatus.Warning => "Caution",
        _ => "Blocked"
    };
    public Brush StatusBrush => Status switch
    {
        CheckStatus.Ready => new SolidColorBrush(Color.FromRgb(101, 201, 135)),
        CheckStatus.Inactive => new SolidColorBrush(Color.FromRgb(126, 136, 126)),
        CheckStatus.Warning => new SolidColorBrush(Color.FromRgb(230, 184, 92)),
        _ => new SolidColorBrush(Color.FromRgb(228, 111, 103))
    };

    public void UpdateFrom(LauncherCheck source)
    {
        bool wasRuntimeWaitingForMod = Id == "hooks" && Status == CheckStatus.Inactive &&
            Detail.StartsWith("Ghost Recon Wildlands is running", StringComparison.Ordinal);
        Set(ref _title, source.Title, nameof(Title));
        Set(ref _detail, source.Detail, nameof(Detail));
        Set(ref _detailToolTip, source.DetailToolTip ?? source.Detail, nameof(DetailToolTip));

        bool statusChanged = Status != source.Status;
        bool isRuntimeWaitingForMod = source.Id == "hooks" && source.Status == CheckStatus.Inactive &&
            source.Detail.StartsWith("Ghost Recon Wildlands is running", StringComparison.Ordinal);
        bool runtimeInactiveLabelChanged = wasRuntimeWaitingForMod != isRuntimeWaitingForMod;
        Status = source.Status;
        if (statusChanged)
        {
            Notify(nameof(Status));
            Notify(nameof(StatusGlyph));
            Notify(nameof(StatusBrush));
        }

        bool analyzing = source.Id == "executable" && source.Detail.StartsWith("Analyzing ", StringComparison.Ordinal);
        if (IsAnalyzing != analyzing)
        {
            IsAnalyzing = analyzing;
            Notify(nameof(IsAnalyzing));
            Notify(nameof(StatusLabel));
        }
        else if (statusChanged || runtimeInactiveLabelChanged)
        {
            Notify(nameof(StatusLabel));
        }

        if (ActionLabel != source.ActionLabel)
        {
            ActionLabel = source.ActionLabel;
            Notify(nameof(ActionLabel));
            Notify(nameof(HasAction));
        }
        if (ActionId != source.ActionId)
        {
            ActionId = source.ActionId;
            Notify(nameof(ActionId));
            Notify(nameof(CanExecuteAction));
        }
    }

    private void Set(ref string field, string value, string propertyName)
    {
        if (field == value) return;
        field = value;
        Notify(propertyName);
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
