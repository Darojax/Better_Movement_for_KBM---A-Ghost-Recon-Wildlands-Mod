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
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.RuntimeActive) && _viewModel.RuntimeActive)
                Dispatcher.BeginInvoke(() => WindowState = WindowState.Minimized);
        };
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _liveCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveCheckTimer.Tick += async (_, _) => await _viewModel.RefreshAsync(recordInLog: false);
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
        SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(0, 31)));
        e.Handled = true;
    }

    public Task ShowAntiCheatManagementAsync(string gameDirectory, bool installed)
    {
        _easyAntiCheatDirectory = Path.Combine(gameDirectory, "EasyAntiCheat");
        AntiCheatStatusHeading.Text = installed ? "SayNoToEAC detected" : "SayNoToEAC not detected";
        AntiCheatStatusDetail.Text = installed
            ? $"Required stub DLLs and original .BAK files were found in {_easyAntiCheatDirectory}"
            : $"The expected stub DLLs and original .BAK files were not found in {_easyAntiCheatDirectory}";
        AntiCheatStatusDetail.ToolTip = AntiCheatStatusDetail.Text;

        Brush statusBrush = installed ? FindBrush("ReadyBrush") : FindBrush("BlockedBrush");
        AntiCheatStatusBorder.BorderBrush = statusBrush;
        AntiCheatStatusCircle.Background = statusBrush;
        AntiCheatStatusGlyph.Text = installed ? "✓" : "×";
        AntiCheatStatusGlyph.RenderTransform = installed
            ? new TranslateTransform(-1, 0)
            : new TranslateTransform(0, -2);

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
        SavePanelHeading.Text = $"{installation.Storefront} save locations";
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
        IReadOnlyList<SaveLocationInfo> locations = SaveBackupService.GetLocations(_saveInstallation);
        SaveLocationsList.ItemsSource = locations;
        SavePanelStatus.Text = message ?? (locations.Count == 0
            ? "No save locations containing this edition's save containers were detected."
            : $"{locations.Count} independent save location{(locations.Count == 1 ? "" : "s")} detected.");
        bool gameRunning = Process.GetProcessesByName("GRW").Length > 0;
        BackUpAllSaveLocationsButton.IsEnabled = locations.Count > 0 && !gameRunning;
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
        SaveBackupService.RemoveCustomLocation(_saveInstallation, location.Id);
        RefreshSavePanel("Custom save location removed from the launcher. No save files or backups were deleted.");
    }

    private async void BackUpAllSaveLocationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saveInstallation is null || Process.GetProcessesByName("GRW").Length > 0) return;
        SavePanelActions.IsEnabled = false;
        SavePanelStatus.Text = "Creating source-separated backups…";
        try
        {
            string destination = await Task.Run(() => SaveBackupService.BackupAll(_saveInstallation));
            RefreshSavePanel($"All save locations were backed up beneath {destination}");
        }
        catch (Exception exception)
        {
            SavePanelStatus.Text = $"Backup failed: {exception.Message}";
        }
        finally
        {
            SavePanelActions.IsEnabled = true;
            RefreshSavePanel(SavePanelStatus.Text);
        }
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
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        WindowPlacementStore.Save(bounds.Left, bounds.Top);
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
