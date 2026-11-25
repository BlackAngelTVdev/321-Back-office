using System.Text.Json;

namespace BackOffice.Mqtt;

/// <summary>
/// Publication d'un log MQTT au contrat d'enveloppe UMDD
/// (<c>doc/contracts/envelope.schema.json</c> et <c>doc/contracts/log.schema.json</c>) :
/// <code>
/// {
///   "v": 1,
///   "id": "…",
///   "from": "COM-0112",
///   "to": "*",
///   "ts": "2026-11-23T09:14:02Z",
///   "type": "log",
///   "payload": { "level": "info", "text": "ma boutique est ouverte" }
/// }
/// </code>
/// </summary>
public static class MqttLogPublisher
{
    /// <summary>Topic d'archivage des logs de Dousville.</summary>
    public const string DefaultTopic = "umddcmid3b/log";

    /// <summary>
    /// Lit le broker comme <see cref="MqttPublisher"/> et le topic dans
    /// BACKOFFICE_MQTT_LOG_TOPIC (défaut : <see cref="DefaultTopic"/>).
    /// Renvoie null si aucun broker n'est configuré.
    /// </summary>
    public static MqttConfig? ConfigFromEnvironment()
    {
        var config = MqttConfig.FromEnvironment("BACKOFFICE_MQTT_LOG_TOPIC");
        if (config is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(config.Topic))
        {
            config.Topic = DefaultTopic;
        }

        return config;
    }

    /// <summary>Publie un log d'information dans une enveloppe UMDD valide.</summary>
    public static bool Publish(
        MqttConfig config,
        string from,
        string text,
        out string? error,
        string level = "info")
    {
        var envelope = new
        {
            v = 1,
            id = Guid.NewGuid().ToString(),
            from,
            to = "*",
            ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            type = "log",
            payload = new { level, text },
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        return MqttPublisher.Publish(config, payload, out error);
    }
}
