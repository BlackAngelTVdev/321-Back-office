namespace BackOffice.Model;

/// <summary>
/// Business Profile (BP) tel que défini par le modèle minimal :
/// - id : ^COM-[0-9]{4}$ (identifie le commerce sans ambiguïté)
/// - name : nom public du commerce
/// - logo : image 16:9 (chemin relatif au fichier JSON, ou data URI base64)
/// - special : produit phare, référencé par son ID produit
/// L'heure n'en fait pas partie : elle est lue dans data/time.json (voir TimeStore).
/// Le back-office étend ce modèle avec la liste des produits.
/// </summary>
public sealed class BusinessProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;

    public string SpecialProductId { get; set; } = string.Empty;
    public List<Product> Products { get; } = new();

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
