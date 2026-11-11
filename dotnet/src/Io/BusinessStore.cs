using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Model;

namespace BackOffice.Io;

public sealed class LoadResult
{
    public bool Ok { get; set; }
    public BusinessProfile Business { get; set; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Sérialisation du business au format échangé :
/// <code>
/// {
///   "ID": "COM-7777",
///   "Name": "Aux Mille Miettes",
///   "Logo": "data:image/webp;base64,...",
///   "Special": { "ID": "PROD-7777", "Name": "Millefeuille", "Picture": "...", "Price": 3.5 },
///   "Products": [ ... ]   // uniquement s'il y a d'autres produits
/// }
/// </code>
/// La lecture accepte aussi l'ancien format (schemaVersion, id/name/logo, special = ID, priceCents).
/// </summary>
public static class BusinessStore
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DirectoryOf(string filePath)
        => Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Directory.GetCurrentDirectory();

    /// <summary>Document JSON normalisé (utilisé aussi par le mode --dump et MQTT).</summary>
    public static JsonObject ToJsonObject(BusinessProfile business, string baseDir, bool embedImages)
    {
        var root = new JsonObject
        {
            ["ID"] = business.Id,
            ["Name"] = business.Name,
            ["Logo"] = ImageReference(business.Logo, baseDir, embedImages),
        };

        var special = business.FindProduct(business.SpecialProductId);
        if (special is not null)
        {
            root["Special"] = ProductToJson(special, baseDir, embedImages);
        }

        // Le produit phare est déjà dans "Special" : "Products" n'apparaît que pour les autres.
        var others = business.Products
            .Where(product => special is null || product.Id != special.Id)
            .ToList();

        if (others.Count > 0)
        {
            var products = new JsonArray();
            foreach (var product in others)
            {
                products.Add(ProductToJson(product, baseDir, embedImages));
            }

            root["Products"] = products;
        }

        return root;
    }

    public static byte[] Serialize(BusinessProfile business, string baseDir, bool embedImages, bool indented)
    {
        var root = ToJsonObject(business, baseDir, embedImages);
        var text = indented ? ToIndentedJson(root) : root.ToJsonString(CompactOptions);
        return Encoding.UTF8.GetBytes(text);
    }

    public static LoadResult Load(string filePath)
    {
        var result = new LoadResult();

        string text;
        try
        {
            text = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            result.Errors.Add($"Impossible d'ouvrir le fichier : {filePath} ({exception.Message})");
            return result;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            result.Errors.Add($"JSON invalide ({exception.Message}).");
            return result;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                result.Errors.Add("Le fichier doit contenir un objet JSON.");
                return result;
            }

            var root = document.RootElement;
            var baseDir = DirectoryOf(filePath);

            var business = new BusinessProfile
            {
                Id = ReadString(root, "ID", "id").Trim(),
                Name = ReadString(root, "Name", "name").Trim(),
                Logo = ReadString(root, "Logo", "logo"),
            };

            if (!Validation.IsValidBusinessId(business.Id))
            {
                result.Errors.Add($"ID de commerce invalide : « {business.Id} » (format attendu COM-XXXX).");
            }

            if (business.Name.Length == 0)
            {
                result.Errors.Add("Le nom du commerce est manquant.");
            }

            if (business.Logo.Length == 0)
            {
                result.Errors.Add("Le logo du commerce est manquant.");
            }
            else if (!ImageLoader.Exists(business.Logo, baseDir))
            {
                result.Warnings.Add($"Logo introuvable : {business.Logo}");
            }

            // Autres produits (facultatif : l'exemple minimal ne contient que le produit phare).
            if (TryGetProperty(root, out var productsElement, "Products", "products")
                && productsElement.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in productsElement.EnumerateArray())
                {
                    index++;
                    business.Products.Add(ReadProduct(item, index, result));
                }
            }

            // Produit phare : objet complet (format actuel) ou simple identifiant (ancien format).
            if (TryGetProperty(root, out var specialElement, "Special", "special"))
            {
                if (specialElement.ValueKind == JsonValueKind.Object)
                {
                    var special = ReadProduct(specialElement, business.Products.Count + 1, result);
                    var existing = business.IndexOfProduct(special.Id);

                    if (existing >= 0)
                    {
                        business.Products[existing] = special;
                    }
                    else
                    {
                        business.Products.Add(special);
                    }

                    business.SpecialProductId = special.Id;
                }
                else if (specialElement.ValueKind == JsonValueKind.String)
                {
                    business.SpecialProductId = (specialElement.GetString() ?? string.Empty).Trim();
                }
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var product in business.Products)
            {
                if (product.Id.Length > 0 && !seenIds.Add(product.Id))
                {
                    result.Errors.Add($"ID produit en double : {product.Id}");
                }

                if (product.Picture.Length == 0)
                {
                    result.Warnings.Add($"Produit {product.Id} : image manquante.");
                }
                else if (!ImageLoader.Exists(product.Picture, baseDir))
                {
                    result.Warnings.Add($"Produit {product.Id} : image introuvable ({product.Picture}).");
                }
            }

            if (business.Products.Count == 0)
            {
                result.Errors.Add("Le business ne contient aucun produit.");
            }

            if (business.SpecialProductId.Length == 0)
            {
                result.Errors.Add("Le produit phare (Special) est manquant.");
            }
            else if (business.IndexOfProduct(business.SpecialProductId) < 0)
            {
                result.Errors.Add(
                    $"Le produit phare « {business.SpecialProductId} » ne fait pas partie de la liste des produits.");
            }

            result.Business = business;
            result.Ok = result.Errors.Count == 0;
        }

        return result;
    }

    /// <summary>Écriture atomique : fichier temporaire puis renommage (pas de fichier corrompu).</summary>
    public static bool Save(string filePath, BusinessProfile business, bool embedImages, out string? error)
    {
        error = null;

        try
        {
            var targetDir = DirectoryOf(filePath);
            Directory.CreateDirectory(targetDir);

            var json = Serialize(business, targetDir, embedImages, indented: true);
            var temporary = filePath + ".tmp";
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, filePath, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            error = $"Impossible d'écrire dans {filePath} ({exception.Message}).";
            return false;
        }
    }

    private static JsonObject ProductToJson(Product product, string baseDir, bool embedImages) => new()
    {
        ["ID"] = product.Id,
        ["Name"] = product.Name,
        ["Picture"] = ImageReference(product.Picture, baseDir, embedImages),
        ["Price"] = product.PriceCents / 100m,
    };

    private static string ImageReference(string reference, string baseDir, bool embedImages)
        => embedImages && reference.Length > 0
            ? ImageLoader.ToDataUri(ImageLoader.ResolveForRead(reference, baseDir)) ?? reference
            : reference;

    /// <summary>Indentation à 4 espaces, comme les fichiers échangés.</summary>
    private static string ToIndentedJson(JsonNode node)
    {
        var json = node.ToJsonString(IndentedOptions);
        var builder = new StringBuilder(json.Length + (json.Length / 2));

        var lines = json.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart(' ').Length;
            builder.Append(' ', indent * 2).Append(line, indent, line.Length - indent);

            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static Product ReadProduct(JsonElement item, int position, LoadResult result)
    {
        var product = new Product
        {
            Id = ReadString(item, "ID", "id").Trim(),
            Name = ReadString(item, "Name", "name").Trim(),
            Picture = ReadString(item, "Picture", "picture"),
            PriceCents = ReadPriceCents(item),
        };

        if (!Validation.IsValidProductId(product.Id))
        {
            result.Errors.Add(
                $"Produit #{position} : ID invalide « {product.Id} » (format attendu PROD-XXXX).");
        }

        if (product.Name.Length == 0)
        {
            result.Errors.Add($"Produit {product.Id} : nom manquant.");
        }

        if (product.PriceCents < 0)
        {
            result.Errors.Add($"Produit {product.Id} : prix invalide (Price attendu, en francs).");
        }

        return product;
    }

    private static int ReadPriceCents(JsonElement item)
    {
        // Ancien format : priceCents (entier).
        if (TryGetProperty(item, out var cents, "priceCents", "PriceCents")
            && cents.ValueKind == JsonValueKind.Number
            && cents.TryGetInt32(out var value))
        {
            return value;
        }

        // Format actuel : Price en francs (3.5 -> 350 centimes).
        if (TryGetProperty(item, out var price, "Price", "price")
            && price.ValueKind == JsonValueKind.Number)
        {
            try
            {
                return (int)Math.Round(price.GetDecimal() * 100m, MidpointRounding.AwayFromZero);
            }
            catch (FormatException)
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(JsonElement element, params string[] names)
        => TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
