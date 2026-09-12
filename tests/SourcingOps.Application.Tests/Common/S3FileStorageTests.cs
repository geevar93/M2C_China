using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SourcingOps.Application.Interfaces;
using SourcingOps.Infrastructure.Storage;

namespace SourcingOps.Application.Tests.Common;

/// <summary>
/// H-19. The S3 client itself is mocked — there is no MinIO in a unit test run, and what is
/// worth pinning here is not that the AWS SDK works but that this class maps relative paths to
/// object keys the same way <see cref="LocalDiskFileStorage"/> maps them to filesystem paths,
/// and that a missing object surfaces as the <see cref="FileNotFoundException"/> callers already
/// handle. Behaviour against a real endpoint stays unproven until the stack runs with
/// <c>--profile minio</c>, in the same way E2-11's external-database path was.
/// </summary>
public class S3FileStorageTests
{
    private readonly Mock<IAmazonS3> _client = new(MockBehavior.Strict);
    private readonly S3FileStorage _sut;

    public S3FileStorageTests()
    {
        _sut = new S3FileStorage(_client.Object, OptionsWithBucket("sourcingops"));
    }

    private static FileStorageOptions OptionsWithBucket(string bucket) =>
        new() { Provider = FileStorageProviders.S3, S3 = new S3StorageOptions { BucketName = bucket } };

    [Fact]
    public async Task SaveAsync_PutsTheObjectUnderTheRelativePathAsKey_AndReturnsIt()
    {
        PutObjectRequest? captured = null;
        _client
            .Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new PutObjectResponse());

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        var stored = await _sut.SaveAsync("catalog-docs/section-1/doc.pdf", input);

        stored.Should().Be("catalog-docs/section-1/doc.pdf");
        captured.Should().NotBeNull();
        captured!.BucketName.Should().Be("sourcingops");
        captured.Key.Should().Be("catalog-docs/section-1/doc.pdf");
    }

    [Fact]
    public async Task SaveAsync_NormalisesLeadingSlashAndBackslashes_ToMatchTheLocalDiskKeyShape()
    {
        // The same document must land on the same key whichever backend wrote it, or a
        // provider switch silently orphans everything already stored.
        PutObjectRequest? captured = null;
        _client
            .Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new PutObjectResponse());

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        await _sut.SaveAsync("\\invoices\\inv-1.pdf", input);

        captured!.Key.Should().Be("invoices/inv-1.pdf");
    }

    /// <summary>
    /// Regression guard. Payload signing was originally disabled unconditionally as a streaming
    /// optimisation; the AWS SDK refuses that over plain HTTP, so every upload to a MinIO
    /// container on a compose network threw "When DisablePayloadSigning is true, the request must
    /// be sent over HTTPS". Found only by running against a real MinIO — a mocked client accepts
    /// the request happily — so it is pinned here as well as in the container test.
    /// </summary>
    [Theory]
    [InlineData("http://minio:9000", false)]
    [InlineData("https://objects.example.com", true)]
    [InlineData("", true)] // no ServiceUrl means real AWS, which the SDK reaches over HTTPS
    public async Task SaveAsync_OnlyDisablesPayloadSigning_WhenTheEndpointIsHttps(string serviceUrl, bool expected)
    {
        PutObjectRequest? captured = null;
        _client
            .Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new PutObjectResponse());

        var sut = new S3FileStorage(
            _client.Object,
            new FileStorageOptions
            {
                Provider = FileStorageProviders.S3,
                S3 = new S3StorageOptions { BucketName = "sourcingops", ServiceUrl = serviceUrl }
            });

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        await sut.SaveAsync("invoices/inv-1.pdf", input);

        captured!.DisablePayloadSigning.Should().Be(expected);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsTheResponseStream()
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        _client
            .Setup(c => c.GetObjectAsync("sourcingops", "invoices/inv-1.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = body });

        await using var stream = await _sut.OpenReadAsync("invoices/inv-1.pdf");
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync()).Should().Be("pdf-bytes");
    }

    [Fact]
    public async Task OpenReadAsync_WhenTheObjectIsMissing_ThrowsFileNotFound()
    {
        // DocumentShareLinkService catches FileNotFoundException specifically and turns it into
        // the uniform 404 that a share link for a deleted document must return. An
        // AmazonS3Exception leaking through instead would surface as a 500.
        _client
            .Setup(c => c.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("no such key") { StatusCode = HttpStatusCode.NotFound });

        var act = async () => await _sut.OpenReadAsync("invoices/gone.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task OpenReadAsync_WhenS3FailsForAnyOtherReason_DoesNotDisguiseItAsAMissingFile()
    {
        // Credentials or connectivity failing must not be swallowed into "the document is gone",
        // which would render every share link a silent 404 with nothing in the logs to explain it.
        _client
            .Setup(c => c.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("access denied") { StatusCode = HttpStatusCode.Forbidden });

        var act = async () => await _sut.OpenReadAsync("invoices/inv-1.pdf");

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    [Fact]
    public async Task DeleteAsync_DeletesByKey()
    {
        _client
            .Setup(c => c.DeleteObjectAsync("sourcingops", "invoices/inv-1.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await _sut.DeleteAsync("invoices/inv-1.pdf");

        _client.VerifyAll();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("catalog-docs/../invoices/inv-1.pdf")]
    public async Task SaveAsync_WithDotSegments_IsRejected(string path)
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        var act = async () => await _sut.SaveAsync(path, input);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetPath_ThrowsRatherThanInventingAFilesystemPath()
    {
        var act = () => _sut.GetPath("invoices/inv-1.pdf");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_WithNoBucketConfigured_FailsLoudly()
    {
        var act = () => new S3FileStorage(_client.Object, OptionsWithBucket(string.Empty));

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- Provider selection --------------------------------------------------------------

    [Fact]
    public void AddFileStorage_DefaultsToLocalDisk()
    {
        var services = new ServiceCollection();
        var options = new FileStorageOptions { RootPath = Path.Combine(Path.GetTempPath(), "sourcingops-" + Guid.NewGuid().ToString("N")) };
        services.AddSingleton(options);

        services.AddFileStorage(options);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileStorage>().Should().BeOfType<LocalDiskFileStorage>();
    }

    [Fact]
    public void AddFileStorage_WithS3Provider_ResolvesTheS3Backend()
    {
        var services = new ServiceCollection();
        var options = new FileStorageOptions
        {
            Provider = FileStorageProviders.S3,
            S3 = new S3StorageOptions { ServiceUrl = "http://minio:9000", BucketName = "sourcingops", AccessKey = "k", SecretKey = "s" }
        };
        services.AddSingleton(options);

        services.AddFileStorage(options);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileStorage>().Should().BeOfType<S3FileStorage>();
    }

    [Fact]
    public void AddFileStorage_WithAnUnknownProvider_ThrowsRatherThanFallingBack()
    {
        var services = new ServiceCollection();
        var act = () => services.AddFileStorage(new FileStorageOptions { Provider = "Azure" });

        act.Should().Throw<InvalidOperationException>();
    }
}
