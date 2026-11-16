namespace BackOffice.Model;

/// <summary>
/// Horaires d'ouverture d'un jour du commerce.
/// <c>Open</c> et <c>Close</c> sont au format « HH:mm » (heure locale) ;
/// les deux vides signifient « fermé ce jour-là ».
/// </summary>
public sealed class OpeningHours
{
    /// <summary>Jours dans l'ordre d'affichage (lundi → dimanche).</summary>
    public static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    };

    public DayOfWeek Day { get; set; }
    public string Open { get; set; } = string.Empty;
    public string Close { get; set; } = string.Empty;

    public bool IsClosed => Open.Length == 0 || Close.Length == 0;

    public OpeningHours Clone() => new() { Day = Day, Open = Open, Close = Close };

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        _ => "Dimanche",
    };

    public static string ShortDayName(DayOfWeek day) => DayName(day)[..3];

    /// <summary>Accepte « Lundi », « lundi », « Monday », « Lun »… (sans casse).</summary>
    public static bool TryParseDay(string? text, out DayOfWeek day)
    {
        day = DayOfWeek.Monday;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        foreach (var candidate in WeekOrder)
        {
            if (string.Equals(DayName(candidate), trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ShortDayName(candidate), trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                day = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Vrai si <paramref name="moment"/> (heure locale) tombe dans une plage d'ouverture.</summary>
    public static bool IsOpen(BusinessProfile business, DateTimeOffset moment)
    {
        var hours = business.HoursFor(moment.DayOfWeek);
        if (hours is null
            || !Validation.TryParseTimeOfDay(hours.Open, out var open)
            || !Validation.TryParseTimeOfDay(hours.Close, out var close))
        {
            return false;
        }

        var time = moment.TimeOfDay;
        return open <= close
            ? time >= open && time < close
            : time >= open || time < close; // plage qui passe minuit
    }
}
