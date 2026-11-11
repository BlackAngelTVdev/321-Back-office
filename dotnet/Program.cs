using System.Text;
using Avalonia;
using BackOffice.Cli;
using BackOffice.Io;

namespace BackOffice;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        EnvFile.Load();

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

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Console.Error.WriteLine($"exception: {eventArgs.ExceptionObject}");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"impossible de démarrer l'interface : {exception}");
            return 1;
        }
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
