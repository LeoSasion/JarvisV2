using Jarvis.DesktopPresence;

namespace Jarvis.ControlCenter;

public static class Program
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        if (App.IsCaptureLaunch(arguments))
        {
            App captureApplication = new(arguments);
            return captureApplication.Run();
        }

        using ControlCenterSingleInstance instance =
            ControlCenterSingleInstance.Acquire();
        if (!instance.IsPrimary)
        {
            return instance.SignalPrimary() ? 0 : 4;
        }

        App application = new(arguments, instance);
        return application.Run();
    }
}
