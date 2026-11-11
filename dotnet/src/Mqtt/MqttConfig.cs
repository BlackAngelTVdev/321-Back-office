namespace BackOffice.Mqtt;

/// <summary>Paramètres d'un client MQTT (broker, topic, identifiants éventuels).</summary>
public sealed class MqttConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Lit BACKOFFICE_MQTT_URL (mqtt://hote:port) ou BACKOFFICE_MQTT_HOST/PORT,
    /// BACKOFFICE_MQTT_USERNAME / PASSWORD, BACKOFFICE_MQTT_CLIENT_ID,
    /// et le topic dans <paramref name="topicVariable"/>.
    /// Renvoie null si aucun broker n'est configuré.
    /// </summary>
    public static MqttConfig? FromEnvironment(string topicVariable)
    {
        var config = new MqttConfig();

        var url = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_URL");
        if (!string.IsNullOrEmpty(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
            {
                return null;
            }

            config.Host = parsed.Host;
            if (parsed.Port > 0)
            {
                config.Port = parsed.Port;
            }
        }
        else
        {
            var host = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_HOST");
            if (string.IsNullOrEmpty(host))
            {
                return null;
            }

            config.Host = host;

            var portText = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_PORT");
            if (int.TryParse(portText, out var port) && port > 0 && port <= 65535)
            {
                config.Port = port;
            }
        }

        config.Topic = Environment.GetEnvironmentVariable(topicVariable) ?? string.Empty;

        var username = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_USERNAME");
        if (!string.IsNullOrEmpty(username))
        {
            config.Username = username;
        }

        var password = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_PASSWORD");
        if (!string.IsNullOrEmpty(password))
        {
            config.Password = password;
        }

        var clientId = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_CLIENT_ID");
        config.ClientId = string.IsNullOrEmpty(clientId)
            ? $"321-backoffice-{Environment.ProcessId}"
            : clientId;

        return config;
    }
}
