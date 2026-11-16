using System.Globalization;
using System.Text.Json;
using BackOffice.Model;

namespace BackOffice.Io;

/// <summary>
/// Heure du commerce, source indépendante du business : les messages du topic MQTT
/// d'heure (voir <see cref="Mqtt.MqttTimeSubscriber"/>) et le fichier « time.json »
/// (<c>data/time.json</c> à côté des données ou du binaire).
/// Le décodage est tolérant (voir <see cref="Parse"/>) : les échantillons de temps
/// peuvent changer de forme sans bloquer l'horloge.
/// Le fichier est relu régulièrement et n'est jamais réécrit.
/// </summary>
public static class TimeStore
{
    public const string FileName = "time.json";

    /// <summary>Clés d'objet qui portent directement l'horodatage (comparaison sans casse).</summary>
    private static readonly HashSet<string> TimeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnixTimeStamp", "UnixTimestamp", "TimeStamp", "Timestamp", "time", "ts",
        "epoch", "epochSeconds", "epochMillis", "seconds", "milliseconds", "ms",
        "date", "datetime", "now", "utc", "utcTime", "sampleTime", "value",
    };

    /// <summary>Profondeur maximale d'exploration d'un échantillon (objets / tableaux imbriqués).</summary>
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
    /// Horodatage (secondes Unix) d'un échantillon de temps, par exemple un message MQTT.
    /// Accepte, dans cet ordre :
    /// <list type="bullet">
    /// <item>un nombre : secondes Unix, ou millisecondes / microsecondes / nanosecondes
    /// (converti automatiquement quand la valeur est trop grande pour des secondes) ;</item>
    /// <item>une chaîne : nombre en texte ou date ISO 8601 (<c>2026-11-16T10:42:07Z</c>,
    /// avec ou sans fuseau) ;</item>
    /// <item>un objet : la première propriété reconnue (<c>UnixTimeStamp</c>, <c>timestamp</c>,
    /// <c>time</c>, <c>ts</c>, <c>date</c>, <c>value</c>…)</item>
    /// <item>un objet ou un tableau enveloppant l'échantillon (<c>{ "sample": { … } }</c>,
    /// <c>[ { … } ]</c>) ; pour un tableau, l'horodatage le plus récent l'emporte.</item>
    /// </list>
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

    /// <summary>Heure à afficher : celle du fichier time.json, sinon l'heure du PC.</summary>
    public static long Resolve(string? businessFile)
        => Read(businessFile) ?? Validation.CurrentUnixTime();

    /// <summary>Explore un élément JSON et renvoie le premier horodatage exploitable.</summary>
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
                // Lot d'échantillons : on garde le plus récent.
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

                // 2. objet enveloppe : n'importe quelle propriété imbriquée (sample, data…).
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
