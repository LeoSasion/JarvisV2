namespace Jarvis.ControlCenter;

public static class Program
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        App application = new(arguments);
        return application.Run();
    }
}
