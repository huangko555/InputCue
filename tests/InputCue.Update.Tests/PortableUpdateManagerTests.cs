using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InputCue.Update;

namespace InputCue.Update.Tests;

public sealed class PortableUpdateManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "InputCue.Update.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckAsyncDownloadsNewerSignedPackage()
    {
        var package = Encoding.UTF8.GetBytes("portable-package");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest("1.1.0", package);
        var signature = Convert.ToBase64String(
            key.SignData(manifest, HashAlgorithmName.SHA256));
        using var manager = CreateManager(key, request => request.AbsolutePath switch
        {
            "/portable-releases.json" => Response(manifest),
            "/portable-releases.json.sig" => Response(Encoding.UTF8.GetBytes(signature)),
            "/InputCue-1.1.0-win-x64-portable.zip" => Response(package),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await manager.CheckAsync(manual: false);

        Assert.True(
            result.Status == PortableUpdateStatus.UpdateReady,
            $"Expected UpdateReady, got {result.Status}: {result.Message}");
        Assert.Equal("1.1.0", result.Version);
        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal(package, await File.ReadAllBytesAsync(result.PackagePath));
    }

    [Fact]
    public async Task CheckAsyncAutomaticCheckIsRateLimitedButManualCheckIsNot()
    {
        var requestCount = 0;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = Encoding.UTF8.GetBytes("portable-package");
        var manifest = CreateManifest("1.0.0", package);
        var signature = Convert.ToBase64String(
            key.SignData(manifest, HashAlgorithmName.SHA256));
        using var manager = CreateManager(key, request =>
        {
            requestCount++;
            return request.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? Response(Encoding.UTF8.GetBytes(signature))
                : Response(manifest);
        });

        var first = await manager.CheckAsync(manual: false);
        var deferred = await manager.CheckAsync(manual: false);
        var manual = await manager.CheckAsync(manual: true);

        Assert.Equal(PortableUpdateStatus.UpToDate, first.Status);
        Assert.Equal(PortableUpdateStatus.Deferred, deferred.Status);
        Assert.Equal(PortableUpdateStatus.UpToDate, manual.Status);
        Assert.Equal(4, requestCount);
        Assert.True(manager.GetAutomaticCheckDelay(DateTimeOffset.UtcNow) > TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task CheckAsyncRejectsManifestWithInvalidSignature()
    {
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = Encoding.UTF8.GetBytes("portable-package");
        var manifest = CreateManifest("1.1.0", package);
        var signature = Convert.ToBase64String(
            otherKey.SignData(manifest, HashAlgorithmName.SHA256));
        using var manager = CreateManager(trustedKey, request =>
            request.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? Response(Encoding.UTF8.GetBytes(signature))
                : Response(manifest));

        var result = await manager.CheckAsync(manual: true);

        Assert.Equal(PortableUpdateStatus.Failed, result.Status);
        Assert.False(Directory.Exists(Path.Combine(_root, "data", "updates")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PortableUpdateManager CreateManager(
        ECDsa key,
        Func<Uri, HttpResponseMessage> responseFactory)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, PortableUpdateManager.PortableMarkerFileName), string.Empty);
        File.WriteAllText(Path.Combine(_root, PortableUpdateManager.UpdaterFileName), string.Empty);
        var client = new HttpClient(new StubHandler(responseFactory));
        return new PortableUpdateManager(
            _root,
            Path.Combine(_root, "data"),
            new Version(1, 0, 0),
            new Uri("https://example.test/portable-releases.json"),
            new Uri("https://example.test/portable-releases.json.sig"),
            client,
            key.ExportSubjectPublicKeyInfoPem());
    }

    private static byte[] CreateManifest(string version, byte[] package) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            version,
            publishedAt = "2026-08-22T00:00:00Z",
            asset = new
            {
                url = $"https://example.test/InputCue-{version}-win-x64-portable.zip",
                sha256 = Convert.ToHexString(SHA256.HashData(package)),
                size = package.Length,
            },
        });

    private static HttpResponseMessage Response(byte[] content) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(content),
    };

    private sealed class StubHandler(Func<Uri, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request.RequestUri!));
    }
}
