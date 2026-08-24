using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Piko.Update;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    ReleaseManifest Manifest);

public sealed class UpdateClient
{
    public static readonly Uri StableManifestUri = new(
        "https://github.com/agentforgehu/piko-desktop-pet/releases/latest/download/update-manifest.json");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;

    public UpdateClient(HttpClient? httpClient = null, Uri? manifestUri = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _manifestUri = manifestUri ?? StableManifestUri;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var current = SemanticVersion.Parse(currentVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Piko", currentVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 64 * 1024)
        {
            throw new InvalidDataException("Update manifest exceeds the response limit.");
        }

        var bytes = await ReadBoundedAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false),
            64 * 1024,
            timeout.Token).ConfigureAwait(false);
        var manifest = ReleaseManifest.Parse(bytes);
        return new UpdateCheckResult(manifest.SemanticVersion.CompareTo(current) > 0, manifest);
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateInstaller installer,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, installer.Url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Piko", "1.0"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
            finalUri.Host is not ("github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com"))
        {
            throw new InvalidDataException("Update download redirected to an untrusted host.");
        }

        if (response.Content.Headers.ContentLength is { } contentLength && contentLength != installer.SizeBytes)
        {
            throw new InvalidDataException("Update installer size does not match the manifest.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long written = 0;
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token).ConfigureAwait(false)) > 0)
                {
                    written = checked(written + read);
                    if (written > installer.SizeBytes)
                    {
                        throw new InvalidDataException("Update installer exceeded its declared size.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }

            if (written != installer.SizeBytes ||
                !string.Equals(
                    Convert.ToHexString(hash.GetHashAndReset()),
                    installer.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Update installer integrity verification failed.");
            }

            return destination;
        }
        catch
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Update response exceeded its limit.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
