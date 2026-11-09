using System.Net.Sockets;
using System.Text;

namespace BackOffice.Mqtt;

public sealed class MqttConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// Publication MQTT 3.1.1 minimale (QoS 0) au-dessus de TcpClient.
/// Suffit pour pousser le business sérialisé vers l'infrastructure de communication.
/// </summary>
public static class MqttPublisher
{
    /// <summary>
    /// Lit BACKOFFICE_MQTT_URL (mqtt://hote:port) ou BACKOFFICE_MQTT_HOST/PORT,
    /// plus BACKOFFICE_MQTT_TOPIC. Renvoie null si la configuration est absente.
    /// </summary>
    public static MqttConfig? ConfigFromEnvironment()
    {
        var topic = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_TOPIC");
        if (string.IsNullOrEmpty(topic))
        {
            return null;
        }

        var config = new MqttConfig { Topic = topic };

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

        var clientId = Environment.GetEnvironmentVariable("BACKOFFICE_MQTT_CLIENT_ID");
        config.ClientId = string.IsNullOrEmpty(clientId)
            ? $"321-backoffice-{Environment.ProcessId}"
            : clientId;

        return config;
    }

    public static bool Publish(MqttConfig config, byte[] payload, out string? error, int timeoutMs = 3000)
    {
        error = null;

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

            stream.Write(ConnectPacket(config.ClientId));
            stream.Flush();

            var response = ReadExact(stream, 4);
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

            stream.Write(PublishPacket(config.Topic, payload));
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

    private static byte[]? ReadExact(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    private static byte[] ConnectPacket(string clientId)
    {
        var body = new List<byte>();
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes("MQTT")));
        body.Add(4);    // niveau de protocole 3.1.1
        body.Add(0x02); // clean session
        body.Add(0x00);
        body.Add(60);   // keepalive 60 s
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes(clientId)));

        var packet = new List<byte> { 0x10 }; // CONNECT
        packet.AddRange(EncodeRemainingLength(body.Count));
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static byte[] PublishPacket(string topic, byte[] payload)
    {
        var body = new List<byte>();
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes(topic)));
        body.AddRange(payload);

        var packet = new List<byte> { 0x30 }; // PUBLISH, QoS 0
        packet.AddRange(EncodeRemainingLength(body.Count));
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static byte[] EncodeRemainingLength(int length)
    {
        var encoded = new List<byte>();

        do
        {
            var value = (byte)(length % 128);
            length /= 128;
            if (length > 0)
            {
                value |= 0x80;
            }

            encoded.Add(value);
        }
        while (length > 0);

        return encoded.ToArray();
    }

    private static byte[] EncodeString(byte[] data)
    {
        var encoded = new byte[data.Length + 2];
        encoded[0] = (byte)((data.Length >> 8) & 0xFF);
        encoded[1] = (byte)(data.Length & 0xFF);
        data.CopyTo(encoded, 2);
        return encoded;
    }
}
