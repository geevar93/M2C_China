using Amazon.S3;
using Amazon.S3.Model;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage behind the same <see cref="IFileStorage"/> seam as
/// <see cref="LocalDiskFileStorage"/> (TECH_SPEC §4.6). Written against MinIO, which speaks the
/// S3 API, so the AWS SDK is the client and no MinIO-specific library is involved — the same
/// class works unchanged against real S3 if the deployment ever moves there.
///
/// <para><b>Why this exists (H-19).</b> WhatsApp dispatch puts a temporary public URL to a PDF
/// in the pre-filled message, and that URL is served by <c>SharedDocumentsController</c> reading
/// the file back through this interface. On local disk that ties every shared document to one
/// container's mounted volume. The owner asked for the shared documents to live in
/// S3-compatible object storage instead.</para>
///
/// <para><b>The relative path is the object key, unchanged.</b> Callers already pass
/// "invoices/{id}.pdf" and "catalog-docs/{sectionId}/{documentId}-{filename}"; those are valid
/// S3 keys as they stand, so nothing above this class needs to know which backend is active and
/// a deployment can switch between them without a data migration beyond copying the files.</para>
///
/// <para><b>Path traversal is still guarded, for a different reason.</b> S3 has no directories
/// and no parent, so "../" cannot escape a bucket the way it escapes a filesystem root — it is
/// simply part of a literal key. The normalisation below is therefore not a containment control
/// but a consistency one: it keeps keys identical to the ones the local-disk backend would
/// produce, so a document saved under one backend is findable under the other.</para>
///
/// <para><b>Streaming, not buffering.</b> <see cref="OpenReadAsync"/> hands back the live
/// response stream so an anonymous share-link download flows straight through to the recipient
/// rather than materialising a whole PDF in the API's memory per request.</para>
/// </summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _disablePayloadSigning;

    public S3FileStorage(IAmazonS3 client, FileStorageOptions options)
    {
        _client = client;
        _bucket = string.IsNullOrWhiteSpace(options.S3.BucketName)
            ? throw new InvalidOperationException("Storage:S3:BucketName must be set when Storage:Provider is 'S3'.")
            : options.S3.BucketName;

        // Payload signing can only be skipped over HTTPS — the SDK refuses outright otherwise
        // ("When DisablePayloadSigning is true, the request must be sent over HTTPS"), because
        // the payload hash is the only integrity check left once TLS is gone. A MinIO container
        // on a compose network is reached over plain HTTP, so the optimisation has to be
        // conditional; enabling it unconditionally makes every single upload throw.
        _disablePayloadSigning = IsHttps(options.S3.ServiceUrl);
    }

    /// <summary>
    /// An empty ServiceUrl means real AWS, which the SDK reaches over HTTPS by default.
    /// </summary>
    private static bool IsHttps(string serviceUrl) =>
        string.IsNullOrWhiteSpace(serviceUrl)
        || serviceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var key = ToKey(relativePath);

        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = content,
                // Skips hashing the whole stream up front where TLS already covers integrity.
                // False over plain HTTP — see the constructor.
                DisablePayloadSigning = _disablePayloadSigning
            },
            ct);

        return key;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var key = ToKey(relativePath);

        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Callers — DocumentShareLinkService in particular — already handle FileNotFoundException
            // as "the row points at a file that is gone", and turn it into a uniform 404. Translating
            // here keeps that behaviour identical across both backends.
            throw new FileNotFoundException("The requested object does not exist.", key, ex);
        }
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        // S3 delete is already idempotent — deleting a missing key succeeds — which matches
        // LocalDiskFileStorage's "delete if it exists" behaviour without a prior existence check.
        await _client.DeleteObjectAsync(_bucket, ToKey(relativePath), ct);
    }

    /// <summary>
    /// Deliberately unsupported. The interface's contract is "an absolute filesystem path", and
    /// an object in a bucket has none; returning a URL instead would silently hand callers
    /// something with entirely different semantics. No production code path calls this — see
    /// <see cref="IFileStorage.GetPath"/> — so failing loudly is better than inventing a value.
    /// </summary>
    public string GetPath(string relativePath) =>
        throw new NotSupportedException(
            "S3FileStorage has no local filesystem path. Use OpenReadAsync to read object content.");

    private static string ToKey(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        }

        var key = relativePath.Replace('\\', '/').TrimStart('/');

        // "a/../b" would be a literal key here, not a traversal, but it would also be a *different*
        // key from the one the local backend resolves to. Rejecting the handful of paths that
        // differ between backends is cheaper than debugging a document that is findable under one
        // storage provider and not the other.
        if (key.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new UnauthorizedAccessException("Relative path must not contain '.' or '..' segments.");
        }

        return key;
    }
}
