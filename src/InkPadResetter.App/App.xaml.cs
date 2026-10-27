using System.IO;
using System.Windows;
using InkPadResetter.Core;

namespace InkPadResetter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless helpers (the app has no console, so they write to a file):
        //   --read <report.txt>   read status and counters only, never writes to the printer
        //   --render <folder>     save PNGs of each window state, no printer needed
        switch (e.Args)
        {
            case ["--read", var report]:
                File.WriteAllText(report, Cli.ReadReport(Resetter.ForUsb()));
                Shutdown();
                return;
            case ["--render", var folder]:
                Cli.RenderStates(folder);
                Shutdown();
                return;
        }

        var window = new MainWindow(Resetter.ForUsb());
        window.Loaded += async (_, _) => await window.RefreshAsync();
        MainWindow = window;
        window.Show();
    }
}
