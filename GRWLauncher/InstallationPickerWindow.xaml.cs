using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GRWBetterMovementLauncher.Services;
using Microsoft.Win32;

namespace GRWBetterMovementLauncher;

public partial class InstallationPickerWindow : Window
{
    public InstallationPickerWindow(IReadOnlyList<GameInstallation> installations, string? currentDirectory)
    {
        InitializeComponent();
        InstallationList.ItemsSource = installations;
        InstallationList.SelectedItem = installations.FirstOrDefault(item =>
            string.Equals(item.Directory, currentDirectory, StringComparison.OrdinalIgnoreCase));
        if (InstallationList.SelectedItem is null && installations.Count > 0) InstallationList.SelectedIndex = 0;
    }

    public GameInstallation? SelectedInstallation { get; private set; }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InstallationList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UseSelectedButton.IsEnabled = InstallationList.SelectedItem is GameInstallation;

    private void InstallationList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InstallationList.SelectedItem is GameInstallation) AcceptSelected();
    }

    private void UseSelectedButton_Click(object sender, RoutedEventArgs e) => AcceptSelected();

    private void AcceptSelected()
    {
        if (InstallationList.SelectedItem is not GameInstallation installation) return;
        SelectedInstallation = installation;
        DialogResult = true;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select the folder containing GRW.exe",
            Multiselect = false,
            InitialDirectory = (InstallationList.SelectedItem as GameInstallation)?.Directory
        };
        if (dialog.ShowDialog(this) != true) return;
        GameInstallation? installation = GameLocator.FromDirectory(dialog.FolderName);
        if (installation is null)
        {
            MessageBox.Show(this, "The selected folder does not contain GRW.exe.", "Invalid Ghost Recon Wildlands folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedInstallation = installation;
        DialogResult = true;
    }
}
