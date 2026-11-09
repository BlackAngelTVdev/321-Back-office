using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Model;

public static class Validation
{
    private static readonly Regex BusinessIdPattern = new("^COM-[0-9]{4}$", RegexOptions.Compiled);
    private static readonly Regex ProductIdPattern = new("^PROD-[0-9]{4}$", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new("^([0-9]{1,2})[:hH.]([0-9]{2})(?:[:.][0-9]{2})?$", RegexOptions.Compiled);

    /// <summary>^COM-[0-9]{4}$</summary>
    public static bool IsValidBusinessId(string? id) => id is not null && BusinessIdPattern.IsMatch(id);

    /// <summary>^PROD-[0-9]{4}$</summary>
    public static bool IsValidProductId(string? id) => id is not null && ProductIdPattern.IsMatch(id);

    /// <summary>Heure du PC au format HH:mm (utilisée quand aucune heure n'est importée).</summary>
    public static string CurrentTime()
        => DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Accepte 9:05, 09:05, 09h05, 09:05:00 et renvoie "09:05" (null si invalide).</summary>
    public static string? NormalizeTime(string? time)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            return null;
        }

        var match = TimePattern.Match(time.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        return hours <= 23 && minutes <= 59
            ? $"{hours:D2}:{minutes:D2}"
            : null;
    }

    /// <summary>Heure valide au format HH:mm.</summary>
    public static bool IsValidTime(string? time) => NormalizeTime(time) is not null;

    /// <summary>2450 -> "24.50 CHF"</summary>
    public static string FormatPrice(int priceCents)
        => $"{priceCents / 100}.{Math.Abs(priceCents % 100):D2} CHF";
}
