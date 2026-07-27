using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Storage;

/// <summary>
/// Writes to a mounted Docker volume under <see cref="FileStorageOptions.RootPath"/>
/// (TECH_SPEC §4.6). Path convention for callers: "catalog-docs/{catalogSectionId}/{documentId}-{originalFilename}",
/// "invoices/{invoiceId}.pdf", "shipment-docs/{shipmentId}/{filename}".
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(FileStorageOptions options)
    {
        _root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var fullPath = ResolveAndGuard(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fileStream, ct);

        return NormalizeRelative(relativePath);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveAndGuard(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested file does not exist.", relativePath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolveAndGuard(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetPath(string relativePath) => ResolveAndGuard(relativePath);

    /// <summary>
    /// Resolves a caller-supplied relative path against the storage root and verifies
    /// the result cannot escape the root (defence in depth against path traversal —
    /// TECH_SPEC §8 — even if a caller forgot to sanitize the original filename first).
    /// </summary>
    private string ResolveAndGuard(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        }

        var combined = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('\\', '/').TrimStart('/')));

        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal) && !string.Equals(combined, _root, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Resolved path escapes the configured storage root.");
        }

        return combined;
    }

    private static string NormalizeRelative(string relativePath) => relativePath.Replace('\\', '/').TrimStart('/');
}
