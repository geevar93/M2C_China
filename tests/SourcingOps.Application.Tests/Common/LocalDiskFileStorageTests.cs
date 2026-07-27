using System.Text;
using FluentAssertions;
using SourcingOps.Infrastructure.Storage;

namespace SourcingOps.Application.Tests.Common;

public class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDiskFileStorage _sut;

    public LocalDiskFileStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sourcingops-tests-" + Guid.NewGuid().ToString("N"));
        _sut = new LocalDiskFileStorage(new FileStorageOptions { RootPath = _root });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsContent()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        await using var input = new MemoryStream(content);

        await _sut.SaveAsync("catalog-docs/section-1/doc.pdf", input);

        await using var readStream = await _sut.OpenReadAsync("catalog-docs/section-1/doc.pdf");
        using var reader = new StreamReader(readStream);
        var text = await reader.ReadToEndAsync();

        text.Should().Be("hello world");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        await _sut.SaveAsync("invoices/inv-1.pdf", input);

        await _sut.DeleteAsync("invoices/inv-1.pdf");

        var act = async () => await _sut.OpenReadAsync("invoices/inv-1.pdf");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\win.ini")]
    public void GetPath_WithPathTraversalAttempt_ThrowsInsteadOfEscapingRoot(string maliciousPath)
    {
        var act = () => _sut.GetPath(maliciousPath);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetPath_WithNormalRelativePath_ResolvesUnderRoot()
    {
        var resolved = _sut.GetPath("shipment-docs/ship-1/packing-list.pdf");

        resolved.Should().StartWith(Path.GetFullPath(_root));
    }

    [Fact]
    public void GetPath_WithLeadingSlash_IsTreatedAsRelativeAndStaysUnderRoot()
    {
        // A leading "/" does not make the caller-supplied path absolute — it is
        // stripped and the result is still resolved (and verified) under the storage
        // root, rather than being interpreted as a filesystem-root-relative path.
        var resolved = _sut.GetPath("/etc/passwd");

        resolved.Should().StartWith(Path.GetFullPath(_root));
    }
}
