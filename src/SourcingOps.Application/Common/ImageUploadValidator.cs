namespace SourcingOps.Application.Common;

/// <summary>
/// Validates an uploaded raster image by its leading bytes (JPEG, PNG or WebP) rather than
/// trusting the client-declared content type, mirroring <see cref="PdfUploadValidator"/>.
/// Returns the sniffed content type so the stored record never carries a spoofed one.
/// </summary>
public static class ImageUploadValidator
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    /// <summary>Returns the detected content type, or null when the bytes are not a supported image.</summary>
    public static string? Sniff(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return Jpeg;
        }
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return Png;
        }
        if (header.Length >= 12 &&
            header[..4].SequenceEqual("RIFF"u8) &&
            header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return Webp;
        }
        return null;
    }

    public static string ExtensionFor(string contentType) => contentType switch
    {
        Png => ".png",
        Webp => ".webp",
        _ => ".jpg"
    };
}
