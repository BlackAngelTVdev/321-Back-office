using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Model;

public static class Validation
{
    private static readonly Regex BusinessIdPattern = new("^COM-[0-9]{4}$", RegexOptions.Compiled);
    private static readonly Regex ProductIdPattern = new("^PROD-[0-9]{4}$", RegexOptions.Compiled);

    /// <summary>^COM-[0-9]{4}$</summary>
    public static bool IsValidBusinessId(string? id) => id is not null && BusinessIdPattern.IsMatch(id);

    /// <summary>^PROD-[0-9]{4}$</summary>
    public static bool IsValidProductId(string? id) => id is not null && ProductIdPattern.IsMatch(id);

    /// <summary>Plus grand timestamp accepté : 9999-12-31T23:59:59Z.</summary>
    private const long MaxUnixTimeStamp = 253402300799;

    /// <summary>Horodatage Unix courant (secondes), utilisé quand aucune heure n'est importée.</summary>
    public static long CurrentUnixTime() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Vrai si le timestamp correspond à une date représentable.</summary>
    public static bool IsValidUnixTime(long unixTimeStamp)
        => unixTimeStamp >= 0 && unixTimeStamp <= MaxUnixTimeStamp;

    /// <summary>Secondes Unix -> heure locale « HH:mm:ss » (« — » si invalide).</summary>
    public static string FormatUnixTime(long unixTimeStamp)
        => IsValidUnixTime(unixTimeStamp)
            ? DateTimeOffset.FromUnixTimeSeconds(unixTimeStamp).ToLocalTime()
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";

    /// <summary>« 7:30 » ou « 07:30 » -> heure de la journée ; false si le format est invalide.</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(text)
            && TimeSpan.TryParseExact(
                text.Trim(),
                new[] { @"h\:mm", @"hh\:mm" },
                CultureInfo.InvariantCulture,
                out time)
            && time >= TimeSpan.Zero
            && time < TimeSpan.FromDays(1);
    }

    /// <summary>TimeSpan -> « HH:mm ».</summary>
    public static string FormatTimeOfDay(TimeSpan time)
        => $"{time.Hours:D2}:{time.Minutes:D2}";

    /// <summary>2450 -> "24.50 CHF"</summary>
    public static string FormatPrice(int priceCents)
        => $"{priceCents / 100}.{Math.Abs(priceCents % 100):D2} CHF";
}
