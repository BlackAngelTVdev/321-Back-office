using System.Text;
using Avalonia;
using BackOffice.Cli;

namespace BackOffice;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--dump")
        {
            UseUtf8Console();
            return CliCommands.Dump(args[1]);
        }

        if (args.Length >= 2 && args[0] == "--publish")
        {
            UseUtf8Console();
            return CliCommands.Publish(args[1]);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void UseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Sortie redirigée vers un tuyau : l'encodage par défaut suffit.
        }
    }
}
