using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Workit.Api.Services;

namespace Workit.Tests.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "workit-attach-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        var options = new StorageOptions { LocalPath = _root };
        _storage = new LocalFileStorageService(options, new StubWebHostEnvironment());
    }

    [Fact]
    public async Task Save_Then_Get_RoundTripsBytesAndContentType()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await _storage.SaveAsync("company/job/file.png", new MemoryStream(bytes), "image/png");

        var stored = await _storage.GetAsync("company/job/file.png");

        stored.Should().NotBeNull();
        stored!.ContentType.Should().Be("image/png");
        using var ms = new MemoryStream();
        await stored.Content.CopyToAsync(ms);
        ms.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenMissing()
    {
        (await _storage.GetAsync("nope/missing.pdf")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        await _storage.SaveAsync("a/b/c.pdf", new MemoryStream([9]), "application/pdf");
        await _storage.DeleteAsync("a/b/c.pdf");
        (await _storage.GetAsync("a/b/c.pdf")).Should().BeNull();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    public async Task Save_RejectsPathTraversalKeys(string key)
    {
        var act = async () => await _storage.SaveAsync(key, new MemoryStream([1]), "text/plain");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
    }
}
