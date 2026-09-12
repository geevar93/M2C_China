namespace SourcingOps.Infrastructure.Storage;

/// <summary>Bound from configuration section "Storage" (TECH_SPEC §4.6).</summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// Config key: <c>Storage:Provider</c>. <c>LocalDisk</c> (the default) or <c>S3</c>.
    ///
    /// <para>H-19 added the S3 path for MinIO. The default stays local disk deliberately: every
    /// test run, every `dotnet run`, and every developer checkout works with no object store to
    /// stand up, and a deployment opts in with one setting.</para>
    /// </summary>
    public string Provider { get; set; } = FileStorageProviders.LocalDisk;

    /// <summary>Used only when <see cref="Provider"/> is <c>LocalDisk</c>.</summary>
    public string RootPath { get; set; } = "/data/uploads";

    /// <summary>Applies to both providers — it is an intake limit, not a backend detail.</summary>
    public long MaxUploadSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB default

    /// <summary>Used only when <see cref="Provider"/> is <c>S3</c>.</summary>
    public S3StorageOptions S3 { get; set; } = new();
}

public static class FileStorageProviders
{
    public const string LocalDisk = "LocalDisk";
    public const string S3 = "S3";
}

/// <summary>
/// Bound from <c>Storage:S3</c>. Written for MinIO, which speaks the S3 API — hence
/// <see cref="ServiceUrl"/> and <see cref="ForcePathStyle"/>, neither of which a real-AWS
/// configuration needs.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>
    /// The MinIO endpoint, e.g. <c>http://minio:9000</c> inside the compose network. Leave empty
    /// to talk to real AWS S3 in <see cref="Region"/> instead.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public string BucketName { get; set; } = "sourcingops";

    /// <summary>
    /// MinIO serves buckets as <c>{endpoint}/{bucket}/{key}</c>, not as the
    /// <c>{bucket}.{endpoint}</c> virtual-host style AWS defaults to — a DNS name per bucket is
    /// not something a container on a compose network has. Must stay <c>true</c> for MinIO.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Ignored when <see cref="ServiceUrl"/> is set, but the SDK still requires *a* region to
    /// sign requests with, and MinIO accepts whatever it is told.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Config keys <c>Storage:S3:AccessKey</c> / <c>Storage:S3:SecretKey</c> — supplied from the
    /// deployment's <c>.env</c>, never committed (E2-09). Left blank, the SDK falls back to its
    /// normal credential chain (environment, instance profile), which is what a real-AWS
    /// deployment would want.
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Create the bucket at startup if it is absent. Convenient for a fresh MinIO container,
    /// which starts with no buckets at all; a managed deployment where the bucket is provisioned
    /// with its own policy should set this false.
    /// </summary>
    public bool CreateBucketIfMissing { get; set; } = true;
}
