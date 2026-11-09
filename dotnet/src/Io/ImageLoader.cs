using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BackOffice.Io;

public static class ImageLoader
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".gif"] = "image/gif",
    };

    public static bool IsDataUri(string? reference)
        => reference is not null && reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chemin absolu utilisable pour la lecture (data URI et chemins absolus rendus tels quels).
    /// Si le chemin relatif n'existe pas, on tente le dossier parent : c'est ce qui permet à
    /// data/business.json de référencer assets/... situé à la racine du dépôt.
    /// </summary>
    public static string? ResolveForRead(string? reference, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        return IsDataUri(reference) ? reference : ResolveFile(reference, baseDir);
    }

    public static bool Exists(string? reference, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        if (IsDataUri(reference))
        {
            var comma = reference.IndexOf(',');
            if (comma < 0)
            {
                return false;
            }

            try
            {
                return Convert.FromBase64String(reference[(comma + 1)..]).Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return File.Exists(ResolveFile(reference, baseDir));
    }

    /// <summary>Charge une image référencée par un chemin (relatif à baseDir) ou un data URI base64.</summary>
    public static Bitmap? Load(string? reference, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        try
        {
            if (IsDataUri(reference))
            {
                var comma = reference.IndexOf(',');
                if (comma < 0)
                {
                    return null;
                }

                var bytes = Convert.FromBase64String(reference[(comma + 1)..]);
                if (bytes.Length == 0)
                {
                    return null;
                }

                using var memory = new MemoryStream(bytes);
                return new Bitmap(memory);
            }

            var path = ResolveFile(reference, baseDir);
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Convertit un fichier image en data URI base64 (null si illisible).</summary>
    public static string? ToDataUri(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (IsDataUri(reference))
        {
            return reference;
        }

        try
        {
            if (!File.Exists(reference))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(reference);
            if (bytes.Length == 0)
            {
                return null;
            }

            var mime = MimeTypes.TryGetValue(Path.GetExtension(reference), out var known)
                ? known
                : "application/octet-stream";
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Image neutre utilisée quand la référence est absente ou introuvable.</summary>
    public static Bitmap Placeholder(int width, int height)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);
        var bitmap = new WriteableBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[buffer.RowBytes * pixelHeight];
            for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
            {
                pixels[offset] = 0x2B;     // bleu  (#1B212B)
                pixels[offset + 1] = 0x21; // vert
                pixels[offset + 2] = 0x1B; // rouge
                pixels[offset + 3] = 0xFF; // alpha
            }

            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return bitmap;
    }

    private static string ResolveFile(string reference, string baseDir)
    {
        if (Path.IsPathRooted(reference))
        {
            return reference;
        }

        var direct = Path.GetFullPath(Path.Combine(baseDir, reference));
        if (File.Exists(direct))
        {
            return direct;
        }

        try
        {
            var parent = Path.GetFullPath(Path.Combine(baseDir, "..", reference));
            if (File.Exists(parent))
            {
                return parent;
            }
        }
        catch (Exception)
        {
            // Chemin impossible à normaliser : on garde la résolution directe.
        }

        return direct;
    }
}
