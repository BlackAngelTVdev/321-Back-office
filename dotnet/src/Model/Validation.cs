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

    /// <summary>2450 -> "24.50 CHF"</summary>
    public static string FormatPrice(int priceCents)
        => $"{priceCents / 100}.{Math.Abs(priceCents % 100):D2} CHF";
}
