using FluentAssertions;
using SourcingOps.Application.Common;

namespace SourcingOps.Application.Tests.Common;

public class PdfUploadValidatorTests
{
    private const long MaxSize = 1024 * 1024;

    private static Stream PdfStream(int extraBytes = 100)
    {
        var bytes = new byte[4 + extraBytes];
        "%PDF"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    [Fact]
    public async Task ValidateAsync_WithValidPdf_ReturnsValid()
    {
        using var stream = PdfStream();

        var result = await PdfUploadValidator.ValidateAsync("application/pdf", stream.Length, stream, MaxSize);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithWrongContentType_ReturnsInvalid()
    {
        using var stream = PdfStream();

        var result = await PdfUploadValidator.ValidateAsync("image/png", stream.Length, stream, MaxSize);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("application/pdf");
    }

    [Fact]
    public async Task ValidateAsync_WithSpoofedContentTypeButWrongMagicBytes_ReturnsInvalid()
    {
        // Content-type says PDF, but the actual bytes don't start with %PDF — must be rejected.
        var bytes = new byte[500];
        "NOTAPDF!"u8.CopyTo(bytes);
        using var stream = new MemoryStream(bytes);

        var result = await PdfUploadValidator.ValidateAsync("application/pdf", stream.Length, stream, MaxSize);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("%PDF");
    }

    [Fact]
    public async Task ValidateAsync_OverSizeCap_ReturnsInvalid()
    {
        using var stream = PdfStream();

        var result = await PdfUploadValidator.ValidateAsync("application/pdf", MaxSize + 1, stream, MaxSize);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("exceeds");
    }

    [Fact]
    public async Task ValidateAsync_EmptyFile_ReturnsInvalid()
    {
        using var stream = new MemoryStream();

        var result = await PdfUploadValidator.ValidateAsync("application/pdf", 0, stream, MaxSize);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_DoesNotConsumeCallersStreamPosition()
    {
        using var stream = PdfStream();
        var originalLength = stream.Length;

        await PdfUploadValidator.ValidateAsync("application/pdf", stream.Length, stream, MaxSize);

        stream.Position.Should().Be(0);
        stream.Length.Should().Be(originalLength);
    }
}
