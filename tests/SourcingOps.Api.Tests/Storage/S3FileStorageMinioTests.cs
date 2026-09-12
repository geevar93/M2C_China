using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Storage;
using Testcontainers.Minio;

namespace SourcingOps.Api.Tests.Storage;

/// <summary>
/// H-19 — <see cref="S3FileStorage"/> against a real MinIO container, not a mocked client.
///
/// <para><b>Why this is here and not beside the unit tests.</b> The unit tests pin the mapping
/// from relative path to object key and the exception translation; they cannot tell you whether
/// the request the SDK actually puts on the wire is one MinIO accepts. That is exactly the class
/// of evidence gap E2-11 left open against Neon (N-44) and that DR-11 asks Testcontainers to
/// close for Postgres — path-style addressing, signing with a made-up region, and payload
/// signing disabled are all things that either work against the real server or do not.</para>
///
/// <para>Requires Docker on the runner, same as every other test in this project.</para>
/// </summary>
public sealed class S3FileStorageMinioTests : IAsyncLifetime
{
    private const string AccessKey = "minio-test-user";
    private const string SecretKey = "minio-test-password";
    private const string Bucket = "sourcingops-test";

    // The Testcontainers module's default image is `minio/minio` on Docker Hub, which no longer
    // resolves ("pull access denied ... repository does not exist"). quay.io is where MinIO
    // publishes; same pinned release as docker-compose.yml, so the suite exercises the image the
    // deployment actually runs.
    private const string MinioImage = "quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z";

    private readonly MinioContainer _minio = new MinioBuilder(MinioImage)
        .WithUsername(AccessKey)
        .WithPassword(SecretKey)
        .Build();

    private ServiceProvider _provider = null!;
    private IFileStorage _sut = null!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var options = new FileStorageOptions
        {
            Provider = FileStorageProviders.S3,
            S3 = new S3StorageOptions
            {
                ServiceUrl = _minio.GetConnectionString(),
                BucketName = Bucket,
                AccessKey = AccessKey,
                SecretKey = SecretKey,
                ForcePathStyle = true,
                CreateBucketIfMissing = true
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddFileStorage(options);
        _provider = services.BuildServiceProvider();

        // The same call Program.cs makes at startup — so this also covers the bucket
        // bootstrap, which is the step a fresh MinIO container needs and a mock cannot prove.
        await _provider.EnsureFileStorageBucketAsync();

        _sut = _provider.GetRequiredService<IFileStorage>();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task EnsureBucket_IsIdempotent()
    {
        // Every API restart calls this; the second run must not fail on an existing bucket.
        var act = async () => await _provider.EnsureFileStorageBucketAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsContent()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        await using var input = new MemoryStream(content);

        var key = await _sut.SaveAsync("catalog-docs/section-1/doc.pdf", input);
        key.Should().Be("catalog-docs/section-1/doc.pdf");

        await using var readStream = await _sut.OpenReadAsync("catalog-docs/section-1/doc.pdf");
        using var reader = new StreamReader(readStream);

        (await reader.ReadToEndAsync()).Should().Be("hello world");
    }

    [Fact]
    public async Task SaveAsync_OverwritesAnExistingKey()
    {
        await using (var first = new MemoryStream(Encoding.UTF8.GetBytes("v1")))
        {
            await _sut.SaveAsync("invoices/inv-overwrite.pdf", first);
        }

        await using (var second = new MemoryStream(Encoding.UTF8.GetBytes("v2")))
        {
            await _sut.SaveAsync("invoices/inv-overwrite.pdf", second);
        }

        await using var readStream = await _sut.OpenReadAsync("invoices/inv-overwrite.pdf");
        using var reader = new StreamReader(readStream);

        (await reader.ReadToEndAsync()).Should().Be("v2");
    }

    [Fact]
    public async Task OpenReadAsync_ForAMissingObject_ThrowsFileNotFound()
    {
        // The behaviour DocumentShareLinkService depends on to return its uniform 404 when a
        // share link outlives the document it points at.
        var act = async () => await _sut.OpenReadAsync("invoices/never-existed.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheObject_AndIsIdempotent()
    {
        await using (var input = new MemoryStream(Encoding.UTF8.GetBytes("x")))
        {
            await _sut.SaveAsync("invoices/inv-delete.pdf", input);
        }

        await _sut.DeleteAsync("invoices/inv-delete.pdf");
        await _sut.DeleteAsync("invoices/inv-delete.pdf"); // second delete must not throw

        var act = async () => await _sut.OpenReadAsync("invoices/inv-delete.pdf");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task SaveAsync_HandlesAPayloadLargerThanASingleBuffer()
    {
        // Uploads are capped at 20 MB (Storage:MaxUploadSizeBytes); 5 MB is enough to exercise
        // the streaming path with DisablePayloadSigning without making the suite slow.
        var bytes = new byte[5 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        await using var input = new MemoryStream(bytes);

        await _sut.SaveAsync("catalog-docs/section-1/large.pdf", input);

        await using var readStream = await _sut.OpenReadAsync("catalog-docs/section-1/large.pdf");
        await using var buffer = new MemoryStream();
        await readStream.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(bytes);
    }
}
