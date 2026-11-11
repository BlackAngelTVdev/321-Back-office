using System.Net.Sockets;
using System.Text;

namespace BackOffice.Mqtt;

/// <summary>
/// Publication MQTT 3.1.1 minimale (QoS 0) au-dessus de TcpClient.
/// Suffit pour pousser le business sérialisé vers l'infrastructure de communication.
/// </summary>
public static class MqttPublisher
{
    /// <summary>
    /// Lit le broker (BACKOFFICE_MQTT_URL ou BACKOFFICE_MQTT_HOST/PORT), les identifiants
    /// et le topic de publication (BACKOFFICE_MQTT_TOPIC). Renvoie null si le broker manque.
    /// </summary>
    public static MqttConfig? ConfigFromEnvironment()
        => MqttConfig.FromEnvironment("BACKOFFICE_MQTT_TOPIC");

    public static bool Publish(MqttConfig config, byte[] payload, out string? error, int timeoutMs = 3000)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(config.Topic))
        {
            error = "Aucun topic de publication configuré (BACKOFFICE_MQTT_TOPIC).";
            return false;
        }

        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(config.Host, config.Port).Wait(timeoutMs))
            {
                error = $"Connexion à {config.Host}:{config.Port} impossible (délai dépassé).";
                return false;
            }

            using var stream = client.GetStream();
            stream.WriteTimeout = timeoutMs;
            stream.ReadTimeout = timeoutMs;

            stream.Write(MqttWire.ConnectPacket(config.ClientId, config.Username, config.Password));
            stream.Flush();

            var response = MqttWire.ReadExact(stream, 4);
            if (response is null || response[0] != 0x20)
            {
                error = $"Réponse CONNACK absente du broker {config.Host}:{config.Port}.";
                return false;
            }

            if (response[3] != 0x00)
            {
                error = $"Le broker a refusé la connexion (code {response[3]}).";
                return false;
            }

            stream.Write(MqttWire.PublishPacket(config.Topic, payload));
            stream.Flush();

            stream.Write(new byte[] { 0xE0, 0x00 }); // DISCONNECT
            stream.Flush();

            return true;
        }
        catch (Exception exception)
        {
            error = $"Publication impossible vers {config.Host}:{config.Port} ({exception.Message}).";
            return false;
        }
    }
}
