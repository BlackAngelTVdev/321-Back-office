using System.Net.Sockets;
using System.Text;

namespace BackOffice.Mqtt;

/// <summary>
/// Encodage / décodage des paquets MQTT 3.1.1 (CONNECT, SUBSCRIBE, PUBLISH, PINGREQ)
/// au-dessus d'un flux TCP. Aucune dépendance externe.
/// </summary>
internal static class MqttWire
{
    /// <summary>CONNECT avec « clean session », keepalive 60 s et identifiants optionnels.</summary>
    public static byte[] ConnectPacket(string clientId, string? username, string? password)
    {
        var hasUsername = !string.IsNullOrEmpty(username);
        var hasPassword = hasUsername && !string.IsNullOrEmpty(password);

        var body = new List<byte>();
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes("MQTT")));
        body.Add(4);    // niveau de protocole 3.1.1
        body.Add((byte)(0x02 | (hasUsername ? 0x80 : 0x00) | (hasPassword ? 0x40 : 0x00)));
        body.Add(0x00);
        body.Add(60);   // keepalive 60 s
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes(clientId)));

        if (hasUsername)
        {
            body.AddRange(EncodeString(Encoding.UTF8.GetBytes(username!)));
        }

        if (hasPassword)
        {
            body.AddRange(EncodeString(Encoding.UTF8.GetBytes(password!)));
        }

        return Frame(0x10, body);
    }

    /// <summary>SUBSCRIBE d'un topic en QoS 0.</summary>
    public static byte[] SubscribePacket(string topic, int packetId)
    {
        var body = new List<byte>
        {
            (byte)((packetId >> 8) & 0xFF),
            (byte)(packetId & 0xFF),
        };
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes(topic)));
        body.Add(0x00); // QoS demandé : 0

        return Frame(0x82, body);
    }

    public static byte[] PublishPacket(string topic, byte[] payload)
    {
        var body = new List<byte>();
        body.AddRange(EncodeString(Encoding.UTF8.GetBytes(topic)));
        body.AddRange(payload);

        return Frame(0x30, body); // PUBLISH, QoS 0
    }

    public static byte[] PingRequest() => new byte[] { 0xC0, 0x00 };

    /// <summary>Lit un paquet complet ; renvoie son corps (null si le flux est fermé).</summary>
    public static byte[]? ReadPacket(NetworkStream stream, out byte header)
    {
        header = 0;

        var first = ReadExact(stream, 1);
        if (first is null)
        {
            return null;
        }

        header = first[0];

        var length = 0;
        var multiplier = 1;

        for (var index = 0; index < 4; index++)
        {
            var digit = ReadExact(stream, 1);
            if (digit is null)
            {
                return null;
            }

            length += (digit[0] & 0x7F) * multiplier;
            if ((digit[0] & 0x80) == 0)
            {
                break;
            }

            multiplier *= 128;
        }

        return length == 0 ? Array.Empty<byte>() : ReadExact(stream, length);
    }

    public static byte[]? ReadExact(NetworkStream stream, int count)
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

    /// <summary>Topic et charge utile d'un PUBLISH QoS 0.</summary>
    public static (string Topic, byte[] Payload)? ReadPublish(byte[] body)
    {
        if (body.Length < 2)
        {
            return null;
        }

        var topicLength = (body[0] << 8) | body[1];
        if (body.Length < 2 + topicLength)
        {
            return null;
        }

        var topic = Encoding.UTF8.GetString(body, 2, topicLength);
        var payload = new byte[body.Length - 2 - topicLength];
        Array.Copy(body, 2 + topicLength, payload, 0, payload.Length);

        return (topic, payload);
    }

    private static byte[] Frame(byte header, List<byte> body)
    {
        var packet = new List<byte> { header };
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
