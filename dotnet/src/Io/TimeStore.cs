using System.Globalization;
using System.Text.Json;
using BackOffice.Model;

namespace BackOffice.Io;

/// <summary>
/// Heure du commerce, source indépendante du business : les messages du topic MQTT
/// d'heure (voir <see cref="Mqtt.MqttTimeSubscriber"/>) et le fichier « time.json »
/// (<c>data/time.json</c> à côté des données ou du binaire).
/// Les messages MQTT suivent le contrat d'enveloppe UMDD (voir <see cref="ParseSample"/>),
/// le fichier garde son format classique (voir <see cref="Parse"/>).
/// Le fichier est relu régulièrement et n'est jamais réécrit.
/// </summary>
public static class TimeStore
{
    public const string FileName = "time.json";

    /// <summary>Clés d'objet qui portent directement l'horodatage dans time.json (sans casse).</summary>
    private static readonly HashSet<string> TimeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnixTimeStamp", "UnixTimestamp", "TimeStamp", "Timestamp", "time", "ts",
        "epoch", "epochSeconds", "epochMillis", "seconds", "milliseconds", "ms",
        "date", "datetime", "now", "utc", "utcTime", "value",
    };

    /// <summary>Profondeur maximale d'exploration du fichier (objets / tableaux imbriqués).</summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// Chemin du fichier d'heure : BACKOFFICE_TIME_FILE, puis un time.json à côté
    /// du business ouvert, puis data/time.json en remontant depuis le binaire.
    /// </summary>
    public static string? FindFile(string? businessFile)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BACKOFFICE_TIME_FILE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        if (!string.IsNullOrWhiteSpace(businessFile))
        {
            var besideBusiness = Path.Combine(BusinessStore.DirectoryOf(businessFile), FileName);
            if (File.Exists(besideBusiness))
            {
                return besideBusiness;
            }
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "data", FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Horodatage du fichier (secondes Unix) ; null s'il manque, est illisible ou invalide.</summary>
    public static long? Read(string? businessFile)
    {
        var path = FindFile(businessFile);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Horodatage (secondes Unix) d'un contenu de time.json :
    /// <c>{ "UnixTimeStamp": 1794218169 }</c>, un nombre direct ou une date ISO 8601.
    /// null si illisible ou invalide.
    /// </summary>
    public static long? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadUnixTime(document.RootElement, 0);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Horodatage (secondes Unix) d'un message de temps MQTT, au format du contrat
    /// d'enveloppe UMDD (<c>doc/contracts/envelope.schema.json</c>) :
    /// <code>
    /// {
    ///   "v": 1,
    ///   "id": "3f6c2a9e-8b41-4d7a-9c5e-1a2b3c4d5e6f",
    ///   "from": "SERV-time",
    ///   "to": "*",
    ///   "ts": "2026-11-09T09:56:09Z",
    ///   "type": "time",
    ///   "payload": { "timestamp": 1794218169 }
    /// }
    /// </code>
    /// L'heure retenue est <c>payload.timestamp</c> (secondes Unix) ; <c>ts</c> n'est que
    /// l'horodatage d'émission de l'enveloppe et n'est pas utilisé comme heure.
    /// Sont refusés : <c>v</c> absent ou différent de 1, <c>type</c> différent de
    /// <c>time</c> (par exemple une enveloppe de log), <c>payload.timestamp</c> absent,
    /// et les anciens messages plats <c>{ "UnixTimeStamp": … }</c>.
    /// null si le message n'est pas une enveloppe de temps valide.
    /// </summary>
    public static long? ParseSample(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadEnvelope(document.RootElement);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Heure à afficher : celle du fichier time.json, sinon l'heure du PC.</summary>
    public static long Resolve(string? businessFile)
        => Read(businessFile) ?? Validation.CurrentUnixTime();

    /// <summary>Valide l'enveloppe du contrat UMDD et lit <c>payload.timestamp</c>.</summary>
    private static long? ReadEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("v", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var contractVersion)
            || contractVersion != 1)
        {
            return null;
        }

        if (!root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "time", StringComparison.Ordinal))
        {
            return null;
        }

        if (!root.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("timestamp", out var timestamp))
        {
            return null;
        }

        return ReadNumber(timestamp);
    }

    /// <summary>Explore un contenu de time.json et renvoie le premier horodatage exploitable.</summary>
    private static long? ReadUnixTime(JsonElement element, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return ReadNumber(element);

            case JsonValueKind.String:
                return ParseText(element.GetString());

            case JsonValueKind.Array:
            {
                long? latest = null;
                foreach (var item in element.EnumerateArray())
                {
                    if (ReadUnixTime(item, depth + 1) is long value && (latest is null || value > latest))
                    {
                        latest = value;
                    }
                }

                return latest;
            }

            case JsonValueKind.Object:
            {
                // 1. propriété nommée comme un horodatage.
                foreach (var property in element.EnumerateObject())
                {
                    if (TimeKeys.Contains(property.Name)
                        && ReadUnixTime(property.Value, depth + 1) is long direct)
                    {
                        return direct;
                    }
                }

                // 2. objet enveloppe : n'importe quelle propriété imbriquée.
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    {
                        continue;
                    }

                    if (ReadUnixTime(property.Value, depth + 1) is long nested)
                    {
                        return nested;
                    }
                }

                return null;
            }

            default:
                return null;
        }
    }

    private static long? ReadNumber(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (element.TryGetInt64(out var integer))
        {
            return NormalizeToSeconds(integer);
        }

        if (element.TryGetDouble(out var real) && double.IsFinite(real) && real >= 0 && real < 1e18)
        {
            return NormalizeToSeconds((long)Math.Round(real));
        }

        return null;
    }

    /// <summary>Nombre en texte, ou date ISO 8601 (sans fuseau : heure locale).</summary>
    private static long? ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return NormalizeToSeconds(integer);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
            && double.IsFinite(real) && real >= 0 && real < 1e18)
        {
            return NormalizeToSeconds((long)Math.Round(real));
        }

        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                out var parsed)
            && parsed.ToUnixTimeSeconds() >= 0)
        {
            return NormalizeToSeconds(parsed.ToUnixTimeSeconds());
        }

        return null;
    }

    /// <summary>
    /// Ramène une valeur à des secondes Unix : une valeur trop grande pour des secondes
    /// (millisecondes, microsecondes, nanosecondes) est divisée par 1000 jusqu'à être valide.
    /// </summary>
    private static long? NormalizeToSeconds(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        long[] divisors = { 1, 1_000, 1_000_000, 1_000_000_000 };
        foreach (var divisor in divisors)
        {
            var candidate = value / divisor;
            if (Validation.IsValidUnixTime(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
