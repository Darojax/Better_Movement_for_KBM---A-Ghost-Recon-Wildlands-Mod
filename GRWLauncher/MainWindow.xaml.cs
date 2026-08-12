using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Threading;
using GRWBetterMovementLauncher.Services;
using GRWBetterMovementLauncher.ViewModels;
using Microsoft.Win32;

namespace GRWBetterMovementLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly DispatcherTimer _liveCheckTimer;
    private bool _closingAfterRestore;
    private bool _launcherDataRemoved;
    private string? _easyAntiCheatDirectory;
    private TaskCompletionSource? _antiCheatPanelCompletion;
    private GameInstallation? _saveInstallation;
    private TaskCompletionSource? _savePanelCompletion;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowPosition();
        _viewModel = new LauncherViewModel(new ProductionLauncherBackend());
        DataContext = _viewModel;
        _viewModel.ActivityLog.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() => ActivityLogScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _liveCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveCheckTimer.Tick += async (_, _) =>
        {
            if (AntiCheatOverlay.Visibility == Visibility.Visible) RefreshAntiCheatPanelStatus();
            await _viewModel.RefreshAsync(recordInLog: false);
        };
        _liveCheckTimer.Start();
    }

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1 || IsInteractiveElement(e.OriginalSource)) return;
        e.Handled = true;
        DragMove();
    }

    private bool IsInteractiveElement(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null && current != this)
        {
            if (current is ButtonBase or Selector or ComboBoxItem or ListBoxItem or TextBoxBase or ScrollBar or Thumb or Hyperlink)
                return true;
            current = GetParent(current);
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) => element switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(element),
        FrameworkContentElement content => content.Parent,
        ContentElement content => ContentOperations.GetParent(content),
        _ => null
    };

    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e) =>
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CleanupFinalOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseFinalCleanupConfirmation();
            e.Handled = true;
            return;
        }
        if (CleanupOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseCleanupPanel();
            e.Handled = true;
            return;
        }
        if (CautionOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseCautionPanel();
            e.Handled = true;
            return;
        }
        if (SaveBackupOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseSavePanel();
            e.Handled = true;
            return;
        }
        if (AntiCheatOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseAntiCheatPanel();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.System || e.SystemKey != Key.Space) return;
        SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(0, 0)));
        e.Handled = true;
    }

    public Task ShowAntiCheatManagementAsync(string gameDirectory)
    {
        _easyAntiCheatDirectory = Path.Combine(gameDirectory, "EasyAntiCheat");
        RefreshAntiCheatPanelStatus();

        _antiCheatPanelCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LauncherTitleBar.IsEnabled = false;
        LauncherBody.IsEnabled = false;
        AntiCheatOverlay.IsHitTestVisible = true;
        AntiCheatOverlay.Opacity = 0;
        AntiCheatOverlay.Visibility = Visibility.Visible;
        AntiCheatOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        AntiCheatOverlay.Focus();
        return _antiCheatPanelCompletion.Task;
    }

    private void RefreshAntiCheatPanelStatus()
    {
        if (string.IsNullOrWhiteSpace(_easyAntiCheatDirectory)) return;
        string gameDirectory = Directory.GetParent(_easyAntiCheatDirectory)?.FullName ?? "";
        bool installed = ProductionLauncherBackend.SayNoToEacAppearsInstalled(gameDirectory);
        bool backupsAvailable = File.Exists(Path.Combine(_easyAntiCheatDirectory, "EasyAntiCheat_x64.dll.BAK"))
            && File.Exists(Path.Combine(_easyAntiCheatDirectory, "EasyAntiCheat_x86.dll.BAK"));
        AntiCheatStatusHeading.Text = installed ? "SayNoToEAC detected" : "SayNoToEAC not detected";
        AntiCheatStatusDetail.Text = installed
            ? $"Required replacement DLLs were found in {_easyAntiCheatDirectory}"
            : $"The expected SayNoToEAC replacement DLLs were not found in {_easyAntiCheatDirectory}";
        AntiCheatStatusDetail.ToolTip = AntiCheatStatusDetail.Text;

        Brush statusBrush = installed ? FindBrush("ReadyBrush") : FindBrush("BlockedBrush");
        AntiCheatStatusBorder.BorderBrush = statusBrush;
        AntiCheatStatusCircle.Background = statusBrush;
        AntiCheatStatusGlyph.Text = installed ? "✓" : "×";
        AntiCheatStatusGlyph.RenderTransform = installed ? new TranslateTransform(-1, 0) : new TranslateTransform(0, -2);

        AntiCheatBackupStatusHeading.Text = backupsAvailable ? "Original EAC backups detected" : "Original EAC backups not detected";
        AntiCheatBackupStatusDetail.Text = backupsAvailable
            ? $"Both optional original .BAK files were found in {_easyAntiCheatDirectory}"
            : "The optional original .BAK files were not both found.";
        AntiCheatBackupStatusDetail.ToolTip = AntiCheatBackupStatusDetail.Text;
        Brush backupBrush = backupsAvailable ? FindBrush("ReadyBrush") : FindBrush("WarningBrush");
        AntiCheatBackupStatusBorder.BorderBrush = backupBrush;
        AntiCheatBackupStatusCircle.Background = backupBrush;
        AntiCheatBackupStatusGlyph.Text = backupsAvailable ? "✓" : "!";
        AntiCheatBackupStatusGlyph.RenderTransform = backupsAvailable
            ? new TranslateTransform(-1, 0) : new TranslateTransform(0, -1);
    }

    private Brush FindBrush(string key) => (Brush)FindResource(key);

    private void RestoreWindowPosition()
    {
        SavedWindowPosition? saved = WindowPlacementStore.Load();
        if (saved is null) return;

        const double visibleEdge = 80;
        double minLeft = SystemParameters.VirtualScreenLeft - Width + visibleEdge;
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - visibleEdge;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 31;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Clamp(saved.Value.Left, minLeft, maxLeft);
        Top = Math.Clamp(saved.Value.Top, minTop, maxTop);
    }

    private void AntiCheatLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OpenAntiCheatFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_easyAntiCheatDirectory) || !Directory.Exists(_easyAntiCheatDirectory))
        {
            MessageBox.Show(this, "The selected installation does not contain an EasyAntiCheat folder.", "Folder not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_easyAntiCheatDirectory}\"") { UseShellExecute = true });
    }

    private void CloseAntiCheatPanelButton_Click(object sender, RoutedEventArgs e) => CloseAntiCheatPanel();

    private void CloseAntiCheatPanel()
    {
        if (_antiCheatPanelCompletion is null || !AntiCheatOverlay.IsHitTestVisible) return;
        AntiCheatOverlay.IsHitTestVisible = false;
        DoubleAnimation fade = new(0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            AntiCheatOverlay.Visibility = Visibility.Collapsed;
            AntiCheatOverlay.BeginAnimation(OpacityProperty, null);
            AntiCheatOverlay.Opacity = 1;
            LauncherTitleBar.IsEnabled = true;
            LauncherBody.IsEnabled = true;
            _antiCheatPanelCompletion?.TrySetResult();
            _antiCheatPanelCompletion = null;
            _easyAntiCheatDirectory = null;
        };
        AntiCheatOverlay.BeginAnimation(OpacityProperty, fade);
    }

    internal Task ShowSaveManagementAsync(GameInstallation installation)
    {
        _saveInstallation = installation;
        _savePanelCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshSavePanel();
        LauncherTitleBar.IsEnabled = false;
        LauncherBody.IsEnabled = false;
        SaveBackupOverlay.IsHitTestVisible = true;
        SaveBackupOverlay.Opacity = 0;
        SaveBackupOverlay.Visibility = Visibility.Visible;
        SaveBackupOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        SaveBackupOverlay.Focus();
        return _savePanelCompletion.Task;
    }

    private void RefreshSavePanel(string? message = null)
    {
        if (_saveInstallation is null) return;
        bool gameRunning = Process.GetProcessesByName("GRW").Length > 0;
        IReadOnlyList<GameInstallation> installations = GameLocator.FindAll()
            .Append(_saveInstallation)
            .GroupBy(item => item.Storefront, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        IReadOnlyList<SaveLocationInfo> locations = installations
            .SelectMany(SaveBackupService.GetLocations)
            .Select(location => location with
            {
                CanBackUp = !gameRunning,
                BackUpToolTip = gameRunning
                    ? "Exit Ghost Recon Wildlands before creating a backup."
                    : "Creates a new timestamped backup of this save location."
            }).ToArray();
        SaveLocationsList.ItemsSource = locations;
        SavePanelStatus.Text = message ?? (locations.Count == 0
            ? "No save locations for any detected installation were found."
            : $"{locations.Count} independent save source{(locations.Count == 1 ? "" : "s")} detected.");
        if (gameRunning) SavePanelStatus.Text = "Exit Ghost Recon Wildlands before creating a backup.";
        RemoveSaveLocationButton.IsEnabled = SaveLocationsList.SelectedItem is SaveLocationInfo { IsAutomatic: false };
    }

    private void SaveLocationsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveSaveLocationButton.IsEnabled = SaveLocationsList.SelectedItem is SaveLocationInfo { IsAutomatic: false };

    private void AddSaveLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saveInstallation is null) return;
        OpenFolderDialog dialog = new()
        {
            Title = $"Add {_saveInstallation.Storefront} save location",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SaveBackupService.AddCustomLocation(_saveInstallation, dialog.FolderName);
            RefreshSavePanel("Custom save location added. It will retain its own backup identity and history.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not add save location", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveSaveLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveLocationsList.SelectedItem is not SaveLocationInfo { IsAutomatic: false } location) return;
        if (_saveInstallation is null) return;
        SaveBackupService.RemoveCustomLocation(location.Installation, location.Id);
        RefreshSavePanel("Custom save location removed from the launcher. No save files or backups were deleted.");
    }

    private async void BackUpSaveLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saveInstallation is null || sender is not Button { Tag: SaveLocationInfo location } ||
            Process.GetProcessesByName("GRW").Length > 0) return;
        SaveLocationsList.IsHitTestVisible = false;
        SavePanelActions.IsEnabled = false;
        SavePanelStatus.Text = $"Creating a new timestamped backup of {location.Name}…";
        try
        {
            string destination = await Task.Run(() => SaveBackupService.BackupLocation(location.Installation, location.Id));
            RefreshSavePanel($"{location.Name} was backed up to {destination}");
        }
        catch (Exception exception)
        {
            SavePanelStatus.Text = $"Backup failed: {exception.Message}";
        }
        finally
        {
            SaveLocationsList.IsHitTestVisible = true;
            SavePanelActions.IsEnabled = true;
            RefreshSavePanel(SavePanelStatus.Text);
        }
    }

    private void OpenSaveLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SaveLocationInfo location }) OpenFolder(location.SaveFilesPath, "save location");
    }

    private void OpenSaveBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SaveLocationInfo { LatestBackupPath: not null } location })
            OpenFolder(location.LatestBackupPath, "backup location");
    }

    private void OpenFolder(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            RefreshSavePanel($"The {description} is no longer available: {path}");
            return;
        }

        ProcessStartInfo startInfo = new("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo);
    }

    private void CloseSavePanelButton_Click(object sender, RoutedEventArgs e) => CloseSavePanel();

    private void CloseSavePanel()
    {
        if (_savePanelCompletion is null || !SaveBackupOverlay.IsHitTestVisible || !SavePanelActions.IsEnabled) return;
        SaveBackupOverlay.IsHitTestVisible = false;
        DoubleAnimation fade = new(0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            SaveBackupOverlay.Visibility = Visibility.Collapsed;
            SaveBackupOverlay.BeginAnimation(OpacityProperty, null);
            SaveBackupOverlay.Opacity = 1;
            LauncherTitleBar.IsEnabled = true;
            LauncherBody.IsEnabled = true;
            _savePanelCompletion?.TrySetResult();
            _savePanelCompletion = null;
            _saveInstallation = null;
            SaveLocationsList.ItemsSource = null;
        };
        SaveBackupOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void ShowCleanupPanelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RuntimeDetected) return;
        CleanupPanelStatus.Text = "";
        CleanupFinalOverlay.Visibility = Visibility.Collapsed;
        ConfirmCleanupButton.IsEnabled = true;
        CancelCleanupButton.IsEnabled = true;
        LauncherTitleBar.IsEnabled = false;
        LauncherBody.IsEnabled = false;
        CleanupOverlay.IsHitTestVisible = true;
        CleanupOverlay.Opacity = 0;
        CleanupOverlay.Visibility = Visibility.Visible;
        CleanupOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        CleanupOverlay.Focus();
    }

    private void CloseCleanupPanelButton_Click(object sender, RoutedEventArgs e) => CloseCleanupPanel();

    private void CloseCleanupPanel()
    {
        if (!CleanupOverlay.IsHitTestVisible || CleanupOverlay.Visibility != Visibility.Visible || !CancelCleanupButton.IsEnabled) return;
        CleanupOverlay.IsHitTestVisible = false;
        DoubleAnimation fade = new(0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            CleanupOverlay.Visibility = Visibility.Collapsed;
            CleanupOverlay.BeginAnimation(OpacityProperty, null);
            CleanupOverlay.Opacity = 1;
            LauncherTitleBar.IsEnabled = true;
            LauncherBody.IsEnabled = true;
        };
        CleanupOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void ShowFinalCleanupConfirmationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RuntimeDetected) return;
        CleanupOverlay.IsHitTestVisible = false;
        FinalCleanupYesButton.IsEnabled = true;
        FinalCleanupNoButton.IsEnabled = true;
        CleanupFinalOverlay.IsHitTestVisible = true;
        CleanupFinalOverlay.Opacity = 0;
        CleanupFinalOverlay.Visibility = Visibility.Visible;
        CleanupFinalOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)));
        CleanupFinalOverlay.Focus();
    }

    private void FinalCleanupNoButton_Click(object sender, RoutedEventArgs e) => CloseFinalCleanupConfirmation();

    private void CloseFinalCleanupConfirmation()
    {
        if (CleanupFinalOverlay.Visibility != Visibility.Visible || !FinalCleanupNoButton.IsEnabled) return;
        CleanupFinalOverlay.IsHitTestVisible = false;
        DoubleAnimation fade = new(0, TimeSpan.FromMilliseconds(75));
        fade.Completed += (_, _) =>
        {
            CleanupFinalOverlay.Visibility = Visibility.Collapsed;
            CleanupFinalOverlay.BeginAnimation(OpacityProperty, null);
            CleanupFinalOverlay.Opacity = 1;
            CleanupOverlay.IsHitTestVisible = true;
        };
        CleanupFinalOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private async void FinalCleanupYesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RuntimeDetected) return;
        FinalCleanupYesButton.IsEnabled = false;
        FinalCleanupNoButton.IsEnabled = false;
        ConfirmCleanupButton.IsEnabled = false;
        CancelCleanupButton.IsEnabled = false;
        CleanupPanelStatus.Text = "Removing launcher-created data…";
        _liveCheckTimer.Stop();
        bool removed = await _viewModel.RemoveLauncherDataAsync();
        if (!removed)
        {
            CleanupPanelStatus.Text = _viewModel.Activity;
            CleanupFinalOverlay.Visibility = Visibility.Collapsed;
            CleanupFinalOverlay.BeginAnimation(OpacityProperty, null);
            CleanupFinalOverlay.Opacity = 1;
            CleanupFinalOverlay.IsHitTestVisible = false;
            CleanupOverlay.IsHitTestVisible = true;
            ConfirmCleanupButton.IsEnabled = true;
            CancelCleanupButton.IsEnabled = true;
            _liveCheckTimer.Start();
            return;
        }

        _launcherDataRemoved = true;
        Close();
    }

    private void ShowCautionPanelButton_Click(object sender, RoutedEventArgs e)
    {
        LauncherTitleBar.IsEnabled = false;
        LauncherBody.IsEnabled = false;
        CautionOverlay.IsHitTestVisible = true;
        CautionOverlay.Opacity = 0;
        CautionOverlay.Visibility = Visibility.Visible;
        CautionOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        CautionOverlay.Focus();
    }

    private void CloseCautionPanelButton_Click(object sender, RoutedEventArgs e) => CloseCautionPanel();

    private void CloseCautionPanel()
    {
        if (!CautionOverlay.IsHitTestVisible || CautionOverlay.Visibility != Visibility.Visible) return;
        CautionOverlay.IsHitTestVisible = false;
        DoubleAnimation fade = new(0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            CautionOverlay.Visibility = Visibility.Collapsed;
            CautionOverlay.BeginAnimation(OpacityProperty, null);
            CautionOverlay.Opacity = 1;
            LauncherTitleBar.IsEnabled = true;
            LauncherBody.IsEnabled = true;
        };
        CautionOverlay.BeginAnimation(OpacityProperty, fade);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.RuntimeActive && !_closingAfterRestore)
        {
            MessageBoxResult result = MessageBox.Show(
                "Better Movement for KBM is active. Restore the original in-memory instructions and close the launcher?",
                "Better Movement for KBM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            e.Cancel = true;
            await _viewModel.StopRuntimeAsync();
            if (_viewModel.RuntimeActive) return;
            _closingAfterRestore = true;
            Close();
            return;
        }
        _liveCheckTimer.Stop();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _liveCheckTimer.Stop();
        if (!_launcherDataRemoved)
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            WindowPlacementStore.Save(bounds.Left, bounds.Top);
        }
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
