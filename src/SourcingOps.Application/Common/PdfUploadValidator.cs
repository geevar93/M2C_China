namespace SourcingOps.Application.Common;

public sealed record UploadValidationResult(bool IsValid, string? Error)
{
    public static UploadValidationResult Valid() => new(true, null);
    public static UploadValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Server-side upload validation for PDF documents (TECH_SPEC §4.6, §8; FR-CAT-06).
/// Checks content-type, the `%PDF` magic-byte signature, and a configurable size cap.
/// Ready for the E6/E8 upload endpoints (catalog documents, invoices) to call once they exist.
/// </summary>
public static class PdfUploadValidator
{
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    public static async Task<UploadValidationResult> ValidateAsync(
        string? contentType,
        long sizeBytes,
        Stream content,
        long maxSizeBytes,
        CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
        {
            return UploadValidationResult.Invalid("File is empty.");
        }

        if (sizeBytes > maxSizeBytes)
        {
            return UploadValidationResult.Invalid($"File exceeds the maximum allowed size of {maxSizeBytes} bytes.");
        }

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return UploadValidationResult.Invalid("Only application/pdf uploads are accepted.");
        }

        var buffer = new byte[4];
        var canSeek = content.CanSeek;
        var originalPosition = canSeek ? content.Position : 0;

        var read = await ReadFullyAsync(content, buffer, ct);

        if (canSeek)
        {
            content.Position = originalPosition;
        }

        if (read < 4 || !buffer.AsSpan().SequenceEqual(PdfMagicBytes))
        {
            return UploadValidationResult.Invalid("File content does not match a PDF (missing %PDF signature).");
        }

        return UploadValidationResult.Valid();
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
