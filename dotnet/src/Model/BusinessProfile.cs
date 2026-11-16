namespace BackOffice.Model;

/// <summary>
/// Business Profile (BP) tel que défini par le modèle minimal :
/// - id : ^COM-[0-9]{4}$ (identifie le commerce sans ambiguïté)
/// - name : nom public du commerce
/// - logo : image 16:9 (chemin relatif au fichier JSON, ou data URI base64)
/// - special : produit phare, référencé par son ID produit
/// L'heure n'en fait pas partie : elle est lue dans data/time.json (voir TimeStore).
/// Le back-office étend ce modèle avec la liste des produits et les horaires d'ouverture.
/// </summary>
public sealed class BusinessProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;

    public string SpecialProductId { get; set; } = string.Empty;
    public List<Product> Products { get; } = new();

    /// <summary>Horaires d'ouverture (un jour par entrée, semaine complète après chargement).</summary>
    public List<OpeningHours> Hours { get; } = new();

    public OpeningHours? HoursFor(DayOfWeek day)
    {
        foreach (var hours in Hours)
        {
            if (hours.Day == day)
            {
                return hours;
            }
        }

        return null;
    }

    public int IndexOfProduct(string? productId)
    {
        if (string.IsNullOrEmpty(productId))
        {
            return -1;
        }

        for (var i = 0; i < Products.Count; i++)
        {
            if (Products[i].Id == productId)
            {
                return i;
            }
        }

        return -1;
    }

    public Product? FindProduct(string? productId)
    {
        var index = IndexOfProduct(productId);
        return index >= 0 ? Products[index] : null;
    }
}
