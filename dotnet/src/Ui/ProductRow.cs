using Avalonia.Media.Imaging;

namespace BackOffice.Ui;

/// <summary>Ligne affichée dans la liste des produits (icône + texte).</summary>
public sealed class ProductRow
{
    public ProductRow(Bitmap? icon, string label)
    {
        Icon = icon;
        Label = label;
    }

    public Bitmap? Icon { get; }

    public string Label { get; }
}
