using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Piko.Update.Tests;

public sealed class UpdateSecurityTests
{
    [Theory]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", -1)]
    [InlineData("1.0.0-alpha.2", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.2.0", "1.1.9", 1)]
    public void SemanticVersionUsesSemverPrecedence(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right))));
    }

    [Fact]
    public void ManifestRejectsUnknownFieldsAndNonOfficialAssets()
    {
        var json = ValidManifestJson().Replace(
            "https://github.com/agentforgehu/piko-desktop-pet/releases/download/v1.0.0/Piko-Setup.exe",
            "https://example.com/Piko-Setup.exe",
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes(json)));

        var unknown = ValidManifestJson().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"extra\":true", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes(unknown)));
    }

    [Fact]
    public async Task UpdateCheckDoesNotExecuteOrDownloadInstaller()
    {
        var handler = new FakeHandler(ValidManifestJson());
        var client = new UpdateClient(
            new HttpClient(handler),
            new Uri("https://github.com/agentforgehu/piko-desktop-pet/releases/latest/download/update-manifest.json"));

        var result = await client.CheckAsync("0.9.0");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
    }

    [Fact]
    public void UnsignedPackageCannotBeTrustedEvenWhenHashMatches()
    {
        var path = typeof(UpdateSecurityTests).Assembly.Location;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var installer = new UpdateInstaller(
            new Uri("https://github.com/agentforgehu/piko-desktop-pet/releases/download/v1.0.0/Piko-Setup.exe"),
            hash,
            new FileInfo(path).Length,
            true);

        var result = UpdatePackageVerifier.Verify(path, installer, ["001122"]);

        Assert.False(result.IsTrusted);
        Assert.Equal("authenticode_invalid", result.Reason);
    }

    [Fact]
    public async Task DownloadRequiresExactDeclaredSizeAndHash()
    {
        var bytes = Encoding.UTF8.GetBytes("bounded installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var handler = new BinaryHandler(bytes);
        var client = new UpdateClient(new HttpClient(handler));
        var root = Path.Combine(Path.GetTempPath(), "Piko.Update.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Piko-Setup.exe");
        try
        {
            var installer = new UpdateInstaller(
                new Uri("https://github.com/agentforgehu/piko-desktop-pet/releases/download/v1.0.0/Piko-Setup.exe"),
                hash,
                bytes.Length,
                true);

            await client.DownloadInstallerAsync(installer, path);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IntegrityFailureDeletesPartialInstaller()
    {
        var bytes = Encoding.UTF8.GetBytes("tampered");
        var handler = new BinaryHandler(bytes);
        var client = new UpdateClient(new HttpClient(handler));
        var root = Path.Combine(Path.GetTempPath(), "Piko.Update.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Piko-Setup.exe");
        try
        {
            var installer = new UpdateInstaller(
                new Uri("https://github.com/agentforgehu/piko-desktop-pet/releases/download/v1.0.0/Piko-Setup.exe"),
                new string('a', 64),
                bytes.Length,
                true);

            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadInstallerAsync(installer, path));

            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string ValidManifestJson() => """
        {
          "schemaVersion":1,
          "version":"1.0.0",
          "channel":"stable",
          "publishedAt":"2026-08-24T00:00:00Z",
          "releasePage":"https://github.com/agentforgehu/piko-desktop-pet/releases/tag/v1.0.0",
          "installer":{
            "url":"https://github.com/agentforgehu/piko-desktop-pet/releases/download/v1.0.0/Piko-Setup.exe",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "sizeBytes":1234,
            "authenticodeRequired":true
          }
        }
        """;

    private sealed class FakeHandler(string content) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class BinaryHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
            response.Content.Headers.ContentLength = content.Length;
            return Task.FromResult(response);
        }
    }
}
