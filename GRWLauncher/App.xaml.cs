using System.Windows;

namespace GRWBetterMovementLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0].Equals("--firewall-helper", StringComparison.OrdinalIgnoreCase))
        {
            int exitCode = Services.FirewallService.ExecuteHelper(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : null);
            Shutdown(exitCode);
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
