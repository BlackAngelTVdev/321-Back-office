namespace BackOffice.Model;

/// <summary>
/// Produit vendu par un commerce.
/// - id : ^PROD-[0-9]{4}$ (identifiant non ambigu)
/// - name : nom d'affichage
/// - picture : image carrée (chemin relatif au fichier JSON, ou data URI base64)
/// - priceCents : prix unitaire en francs, stocké en centimes (entier, sans perte)
/// </summary>
public sealed class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public int PriceCents { get; set; }

    public Product Clone() => new()
    {
        Id = Id,
        Name = Name,
        Picture = Picture,
        PriceCents = PriceCents,
    };
}
