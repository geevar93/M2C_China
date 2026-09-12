using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Interfaces;

namespace SourcingOps.Infrastructure.Storage;

/// <summary>
/// Picks the <see cref="IFileStorage"/> backend from <c>Storage:Provider</c> (H-19).
/// Kept out of <see cref="DependencyInjection"/> so the S3 client wiring — which is a page of
/// MinIO-specific detail — does not sit in the middle of the general service registration.
/// </summary>
public static class FileStorageRegistration
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, FileStorageOptions options)
    {
        if (string.Equals(options.Provider, FileStorageProviders.LocalDisk, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
            return services;
        }

        if (!string.Equals(options.Provider, FileStorageProviders.S3, StringComparison.OrdinalIgnoreCase))
        {
            // A typo here would otherwise fall through to a backend the operator did not choose,
            // and the symptom would be documents quietly written to the wrong place.
            throw new InvalidOperationException(
                $"Storage:Provider '{options.Provider}' is not recognised. Use '{FileStorageProviders.LocalDisk}' or '{FileStorageProviders.S3}'.");
        }

        services.AddSingleton<IAmazonS3>(_ => BuildS3Client(options.S3));
        services.AddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }

    /// <summary>
    /// Creates the configured bucket if it is absent. A no-op unless the S3 provider is active
    /// and <see cref="S3StorageOptions.CreateBucketIfMissing"/> is set, so it is safe to call
    /// unconditionally at startup.
    /// </summary>
    public static async Task EnsureFileStorageBucketAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<FileStorageOptions>();
        if (!string.Equals(options.Provider, FileStorageProviders.S3, StringComparison.OrdinalIgnoreCase)
            || !options.S3.CreateBucketIfMissing)
        {
            return;
        }

        var client = services.GetRequiredService<IAmazonS3>();

        // ListBuckets rather than DoesS3BucketExistV2: the latter reports "exists" for a bucket
        // owned by someone else and, on MinIO, for a credential that cannot see it either way.
        var buckets = await client.ListBucketsAsync(ct);
        if (buckets.Buckets?.Any(b => string.Equals(b.BucketName, options.S3.BucketName, StringComparison.Ordinal)) == true)
        {
            return;
        }

        await client.PutBucketAsync(new PutBucketRequest { BucketName = options.S3.BucketName }, ct);
    }

    private static IAmazonS3 BuildS3Client(S3StorageOptions s3)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = s3.ForcePathStyle,
            // MinIO has no regional endpoints; the region is only ever a signing input for it.
            AuthenticationRegion = s3.Region
        };

        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            config.ServiceURL = s3.ServiceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region);
        }

        // Blank credentials are not an error: that is the real-AWS case, where the SDK's default
        // chain (environment, instance profile) is the right source and hard-coding keys is not.
        // MinIO always needs explicit ones, and they come from the deployment's .env (E2-09).
        return string.IsNullOrWhiteSpace(s3.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(s3.AccessKey, s3.SecretKey), config);
    }
}
