using System.Text;
using BackOffice.Io;
using BackOffice.Mqtt;

namespace BackOffice.Cli;

internal static class CliCommands
{
    /// <summary>Valide et affiche le JSON normalisé (code 1 si invalide).</summary>
    public static int Dump(string path)
    {
        var result = BusinessStore.Load(path);

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"avertissement: {warning}");
        }

        if (!result.Ok)
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"erreur: {error}");
            }

            return 1;
        }

        var json = BusinessStore.Serialize(
            result.Business, BusinessStore.DirectoryOf(path), embedImages: false, indented: true);
        Console.WriteLine(Encoding.UTF8.GetString(json));
        return 0;
    }

    /// <summary>Publie le business sur le broker MQTT configuré.</summary>
    public static int Publish(string path)
    {
        var result = BusinessStore.Load(path);
        if (!result.Ok)
        {
            foreach (var message in result.Errors)
            {
                Console.WriteLine($"erreur: {message}");
            }

            return 1;
        }

        var config = MqttPublisher.ConfigFromEnvironment();
        if (config is null)
        {
            Console.WriteLine("erreur: BACKOFFICE_MQTT_URL et BACKOFFICE_MQTT_TOPIC doivent être définis");
            return 1;
        }

        var payload = BusinessStore.Serialize(
            result.Business, BusinessStore.DirectoryOf(path), embedImages: false, indented: false);

        if (!MqttPublisher.Publish(config, payload, out var error))
        {
            Console.WriteLine($"erreur: {error}");
            return 1;
        }

        Console.WriteLine($"publié sur {config.Topic} ({config.Host}:{config.Port})");
        return 0;
    }
}
