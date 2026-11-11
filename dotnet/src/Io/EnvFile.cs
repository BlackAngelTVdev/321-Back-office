namespace BackOffice.Io;

/// <summary>
/// Chargement minimal d'un fichier « .env » (lignes <c>KEY=VALUE</c>, commentaires « # »).
/// Les variables déjà définies dans l'environnement gardent la priorité.
/// </summary>
public static class EnvFile
{
    public const string FileName = ".env";

    /// <summary>
    /// Charge le .env trouvé (BACKOFFICE_ENV_FILE, sinon en remontant depuis le binaire
    /// ou le dossier courant). Renvoie le chemin utilisé, ou null si aucun fichier.
    /// </summary>
    public static string? Load(bool overwrite = false)
    {
        var path = Find();
        if (path is null)
        {
            return null;
        }

        try
        {
            foreach (var (key, value) in Parse(File.ReadAllLines(path)))
            {
                if (!overwrite && Environment.GetEnvironmentVariable(key) is not null)
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }
        catch (Exception)
        {
            return null;
        }

        return path;
    }

    /// <summary>Chemin du .env : BACKOFFICE_ENV_FILE, puis .env à côté du binaire ou du dossier courant (en remontant).</summary>
    public static string? Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BACKOFFICE_ENV_FILE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return File.Exists(fromEnvironment) ? fromEnvironment : null;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Lit les paires KEY=VALUE ; ignore lignes vides, commentaires et guillemets englobants.</summary>
    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            else
            {
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    value = value[..comment].TrimEnd();
                }
            }

            if (key.Length > 0)
            {
                yield return (key, value);
            }
        }
    }
}
