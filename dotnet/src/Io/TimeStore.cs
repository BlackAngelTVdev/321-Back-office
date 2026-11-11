using System.Text.Json;
using BackOffice.Model;

namespace BackOffice.Io;

/// <summary>
/// Heure du commerce, source indépendante du business : fichier « time.json »
/// (<c>data/time.json</c> à côté des données ou du binaire), au format
/// <code>{ "UnixTimeStamp": 1794218169 }</code> (secondes Unix, UTC).
/// Il est relu chaque minute ; s'il est absent ou invalide, l'heure du PC est affichée.
/// Le back-office ne le réécrit jamais.
/// </summary>
public static class TimeStore
{
    public const string FileName = "time.json";

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
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return ReadUnixTime(document.RootElement) is long value && Validation.IsValidUnixTime(value)
                ? value
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Heure à afficher : celle de time.json, sinon l'heure du PC.</summary>
    public static long Resolve(string? businessFile)
        => Read(businessFile) ?? Validation.CurrentUnixTime();

    /// <summary>Lit <c>{ "UnixTimeStamp": int }</c> ou un nombre direct.</summary>
    private static long? ReadUnixTime(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Number)
        {
            return root.TryGetInt64(out var direct) ? direct : null;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "UnixTimeStamp", "UnixTimestamp", "unixtimestamp" })
            {
                if (root.TryGetProperty(name, out var stamp) && stamp.ValueKind == JsonValueKind.Number)
                {
                    return stamp.TryGetInt64(out var value) ? value : null;
                }
            }
        }

        return null;
    }
}
