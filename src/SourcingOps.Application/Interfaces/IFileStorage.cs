namespace SourcingOps.Application.Interfaces;

/// <summary>File storage abstraction (TECH_SPEC §4.6). Local disk today; swappable later without touching business logic.</summary>
public interface IFileStorage
{
    /// <summary>
    /// Saves the stream under the given relative path (e.g. "catalog-docs/{sectionId}/{documentId}-{filename}")
    /// beneath the configured root. Returns the stored relative path.
    /// </summary>
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Resolves the relative path to an absolute filesystem path under the configured root.</summary>
    string GetPath(string relativePath);
}
