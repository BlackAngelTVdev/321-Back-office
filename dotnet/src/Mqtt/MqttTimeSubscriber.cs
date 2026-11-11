using System.Net.Sockets;
using System.Text;
using BackOffice.Io;

namespace BackOffice.Mqtt;

/// <summary>
/// Abonnement MQTT 3.1.1 (QoS 0) à un topic d'heure, par exemple
/// <c>umddcmid3b/time</c> avec <c>{ "UnixTimeStamp": 1794218169 }</c>.
/// Chaque message reçu est décodé puis transmis via <c>onTime</c>.
/// Reconnexion automatique tant que l'abonnement est actif.
/// </summary>
public sealed class MqttTimeSubscriber : IDisposable
{
    private readonly MqttConfig _config;
    private readonly Action<long> _onTime;
    private readonly Action<string>? _onStatus;
    private readonly object _gate = new();

    private Thread? _thread;
    private TcpClient? _client;
    private volatile bool _stopping;

    public MqttTimeSubscriber(MqttConfig config, Action<long> onTime, Action<string>? onStatus = null)
    {
        _config = config;
        _onTime = onTime;
        _onStatus = onStatus;
    }

    /// <summary>Configuration du topic d'heure (BACKOFFICE_MQTT_TIME_TOPIC) ; null si le broker n'est pas défini.</summary>
    public static MqttConfig? ConfigFromEnvironment()
    {
        var config = MqttConfig.FromEnvironment("BACKOFFICE_MQTT_TIME_TOPIC");
        if (config is not null)
        {
            // Identifiant distinct du client de publication, qui vit dans le même processus.
            config.ClientId = $"{config.ClientId}-clock";
        }

        return config;
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "mqtt-time" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stopping = true;

        lock (_gate)
        {
            _client?.Close();
        }

        _thread?.Join(1000);
        _thread = null;
    }

    private void Run()
    {
        while (!_stopping)
        {
            try
            {
                using var client = new TcpClient();
                lock (_gate)
                {
                    _client = client;
                }

                if (!client.ConnectAsync(_config.Host, _config.Port).Wait(5000))
                {
                    throw new IOException($"connexion à {_config.Host}:{_config.Port} impossible (délai dépassé)");
                }

                using var stream = client.GetStream();
                stream.ReadTimeout = 30000;
                stream.WriteTimeout = 5000;

                stream.Write(MqttWire.ConnectPacket(_config.ClientId, _config.Username, _config.Password));
                stream.Flush();

                var connack = MqttWire.ReadExact(stream, 4);
                if (connack is null || connack[0] != 0x20)
                {
                    throw new IOException("réponse CONNACK absente");
                }

                if (connack[3] != 0x00)
                {
                    throw new IOException($"le broker a refusé la connexion (code {connack[3]})");
                }

                stream.Write(MqttWire.SubscribePacket(_config.Topic, 1));
                stream.Flush();

                // Sans cette vérification, un abonnement refusé (ACL, topic invalide)
                // laisserait l'application « connectée » mais muette.
                var subscribed = false;
                for (var attempt = 0; attempt < 5 && !subscribed; attempt++)
                {
                    var subBody = MqttWire.ReadPacket(stream, out var subHeader);
                    if (subBody is null)
                    {
                        break;
                    }

                    if ((subHeader & 0xF0) != 0x90 || subBody.Length < 3)
                    {
                        continue;
                    }

                    if (subBody[^1] == 0x80)
                    {
                        throw new IOException($"le broker refuse l'abonnement à {_config.Topic}");
                    }

                    subscribed = true;
                }

                if (!subscribed)
                {
                    throw new IOException($"abonnement à {_config.Topic} non confirmé par le broker");
                }

                _onStatus?.Invoke(
                    $"MQTT : abonné à {_config.Topic} sur {_config.Host}:{_config.Port}.");

                while (!_stopping)
                {
                    byte header;
                    byte[]? body;

                    try
                    {
                        body = MqttWire.ReadPacket(stream, out header);
                    }
                    catch (IOException)
                    {
                        if (_stopping)
                        {
                            return;
                        }

                        stream.Write(MqttWire.PingRequest());
                        stream.Flush();
                        continue;
                    }

                    if (body is null)
                    {
                        break;
                    }

                    if ((header & 0xF0) != 0x30)
                    {
                        continue;
                    }

                    var publish = MqttWire.ReadPublish(body);
                    if (publish is null)
                    {
                        continue;
                    }

                    var payload = Encoding.UTF8.GetString(publish.Value.Payload);
                    var unixTimeStamp = TimeStore.Parse(payload);
                    if (unixTimeStamp is long value)
                    {
                        _onTime(value);
                    }
                }

                if (!_stopping)
                {
                    _onStatus?.Invoke("MQTT : connexion fermée par le broker, reconnexion…");
                }
            }
            catch (Exception exception)
            {
                if (!_stopping)
                {
                    _onStatus?.Invoke($"MQTT : {exception.Message} ; nouvelle tentative dans 3 s.");
                }
            }
            finally
            {
                lock (_gate)
                {
                    _client = null;
                }
            }

            if (!_stopping)
            {
                Thread.Sleep(3000);
            }
        }
    }
}
