using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Gelato;
using Gelato.Config;
using Gelato.Services;
using Microsoft.Extensions.Logging.Abstractions;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
var handler = new FakeHandler();
var factory = new FakeFactory(handler);
GelatoStremioProvider Provider(string host, GelatoStremioProvider[]? others = null, GelatoStremioProvider? meta = null) => new("https://" + host, factory, NullLogger<GelatoStremioProvider>.Instance, others, meta);
handler.Response = request => request.AbsolutePath.EndsWith("manifest.json")
    ? """{"resources":["meta",{"name":"stream","types":["movie"],"idPrefixes":["tt"]}],"types":["movie","series"],"catalogs":[]}"""
    : """{"meta":{"id":"tt1","name":"Example"}}""";
var p = Provider("first.test");
await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => p.GetManifestAsync()));
Check(handler.Count("first.test/manifest.json") == 1, "manifest requests coalesce");
Check(await p.SupportsAsync("meta", "series", "custom:1"), "string resources inherit manifest types");
Check(await p.SupportsAsync("stream", "movie", "tt1") && !await p.SupportsAsync("stream", "series", "tt1") && !await p.SupportsAsync("stream", "movie", "tmdb:1"), "object resources restrict type and ID prefix");
await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => p.GetMetaAsync("tt1", StremioMediaType.Movie)));
Check(handler.Count("first.test/meta/movie/tt1.json") == 1, "20 identical metadata calls make one request");
var series = await p.GetMetaAsync("tt1", StremioMediaType.Series);
Check(handler.Count("first.test/meta/series/tt1.json") == 1 && series?.Type == StremioMediaType.Series, "metadata caches separate media types and infer missing type");
await p.GetMetaAsync("tt1", StremioMediaType.Movie);
Check(handler.Count("first.test/meta/movie/tt1.json") == 1, "warm metadata lookup makes no request");
await p.GetMetaAsync("ttl", StremioMediaType.Movie, TimeSpan.FromMilliseconds(-1));
await p.GetMetaAsync("ttl", StremioMediaType.Movie);
Check(handler.Count("first.test/meta/movie/ttl.json") == 2, "expired metadata refetches");
var direct = Provider("direct.test"); var routed = Provider("streams.test", meta: direct);
await routed.GetMetaAsync("tt7", StremioMediaType.Movie);
Check(handler.Count("direct.test/meta/movie/tt7.json") == 1 && handler.Count("streams.test/manifest.json") == 0, "AIOMetadata bypasses stream aggregator entirely");
handler.Response = request => request.AbsolutePath.EndsWith("manifest.json")
    ? """{"resources":["stream"],"types":["movie"],"catalogs":[]}"""
    : request.Host == "broken.test" ? throw new HttpRequestException("outage") : """{"streams":[{"url":"https://media.test/movie.mp4"}]}""";
var streams = await Provider("ok.test", [Provider("broken.test"), Provider("other.test")]).GetStreamsAsync(new StremioUri(StremioMediaType.Movie, "tt1"));
Check(streams.Count == 1, "stream failure isolation and duplicate removal");
handler.Response = request => request.AbsolutePath.EndsWith("manifest.json")
    ? """{"resources":["catalog"],"types":["movie"],"catalogs":[{"id":"s","type":"movie","extra":[{"name":"search","isRequired":true}]}]}"""
    : """{"metas":[{"id":"custom:1","name":"Missing type"},{"id":"custom:2","type":"alien","name":"Unsupported type"}]}""";
var search = await Provider("search.test", [Provider("search2.test")]).SearchAsync("a & b", StremioMediaType.Movie);
Check(search.Count == 2 && search[0].Type == StremioMediaType.Movie && search[1].Type == StremioMediaType.Unknown, "unknown types do not poison a catalogue or become movies");
Check(handler.Requests.Keys.Any(k => k.Contains("search=a%20%26%20b")), "search extras escape reserved characters");
Check(handler.Count("search2.test/manifest.json") == 1, "search discovers additional addons");
Check(PluginConfiguration.NormalizeUrl(" stremio://addon.test/AbC/manifest.json ") == "https://addon.test/AbC", "manifest normalization preserves credential case");
Check(Provider("addon.test/AbC").SourceKey != Provider("addon.test/abc").SourceKey, "endpoint identities preserve case");
Check(CatalogPreviewService.Thumbnail("https://image.tmdb.org/t/p/original/a.jpg") == "https://image.tmdb.org/t/p/w342/a.jpg", "TMDB row posters use small variant");
Check(CatalogPreviewService.Thumbnail("https://posters.test/original/a.jpg?token=a") == "https://posters.test/original/a.jpg?token=a", "opaque artwork URLs remain intact");
Check(CatalogPreviewService.Thumbnail("javascript:alert(1)") is null, "invalid artwork schemes rejected");
var cfg = new PluginConfiguration { MetadataUrl = "https://metadata.test", AddonUrls = ["https://private.test"], LazyImages = true, EnableHomeRows = true };
var effective = new UserConfig { Url = "https://user.test" }.ApplyOverrides(cfg);
Check(effective.MetadataUrl == cfg.MetadataUrl && effective.AddonUrls.Count == 0 && effective.LazyImages && effective.EnableHomeRows, "user overrides preserve settings without inheriting private addon URLs");
Check(!new StremioCatalog { Extra = [new() { Name = "genre", IsRequired = true }] }.IsImportable(), "required filters excluded from default rows");
var clock = new TestClock();
var cache = new PreviewCache<int>(2, clock);
var calls = 0;
async Task<int> Fetch() { Interlocked.Increment(ref calls); await Task.Delay(20); return calls; }
var cold = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync("row", TimeSpan.FromSeconds(60), Fetch)));
Check(calls == 1 && cold.All(v => v == 1), "20 cold row reads coalesce into one fetch");
Check(await cache.GetAsync("row", TimeSpan.FromSeconds(60), Fetch) == 1 && calls == 1, "warm row read makes no request");
clock.Advance(61);
var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var stale = await cache.GetAsync("row", TimeSpan.FromSeconds(60), () => release.Task);
Check(stale == 1 && !release.Task.IsCompleted, "expired row returns immediately during background refresh");
release.SetResult(2); await Task.Delay(30);
Check(await cache.GetAsync("row", TimeSpan.FromSeconds(60), Fetch) == 2, "successful refresh replaces row snapshot");
clock.Advance(61);
var failures = 0;
Task<int> Fail() { failures++; throw new HttpRequestException("outage"); }
Check(await cache.GetAsync("row", TimeSpan.FromSeconds(60), Fail) == 2, "outage preserves stale row");
await Task.Delay(30);
Check(await cache.GetAsync("row", TimeSpan.FromSeconds(60), Fail) == 2 && failures == 1, "failed row refresh backs off");
Check(await cache.GetAsync("other-user", TimeSpan.FromSeconds(60), () => Task.FromResult(9)) == 9, "separate user key has separate snapshot");
Check(await cache.GetAsync("third", TimeSpan.FromSeconds(60), () => Task.FromResult(10)) == 10, "bounded cache evicts idle entry");
var slow = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
using var cancellation = new CancellationTokenSource();
var cancelled = cache.GetAsync("cancelled", TimeSpan.FromSeconds(60), () => slow.Task, cancellation.Token);
cancellation.Cancel();
try { await cancelled; Check(false, "cancelled waiter"); } catch (OperationCanceledException) { Check(true, "request cancellation releases row waiter"); }
slow.SetResult(11); await Task.Delay(30);
Check(await cache.GetAsync("cancelled", TimeSpan.FromSeconds(60), Fetch) == 11, "cancelled waiter does not cancel shared refresh");
var temp = Path.Combine(Path.GetTempPath(), "gelato-image-test-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
try
{
    var originalPath = Path.Combine(temp, "Primary.jpg");
    await File.WriteAllTextAsync(originalPath + ".url", "https://image.tmdb.org/t/p/original/poster.jpg");
    var paths = Proxy<MediaBrowser.Common.Configuration.IApplicationPaths>.Make((method, args) => temp);
    string? processedPath = null;
    var processor = Proxy<MediaBrowser.Controller.Drawing.IImageProcessor>.Make((method, args) =>
    {
        var options = (MediaBrowser.Controller.Drawing.ImageProcessingOptions)args![0]!;
        processedPath = options.Image.Path;
        return Task.FromResult((processedPath, (string?)"image/jpeg", DateTime.UtcNow));
    });
    var decorator = new Gelato.Decorators.ImageProcessorDecorator(processor, paths,
        new Lazy<Gelato.Decorators.ProviderManagerDecorator>(() => throw new Exception("Should not hydrate original")),
        new Lazy<MediaBrowser.Controller.Library.ILibraryManager>(() => throw new Exception("Should not write metadata")),
        NullLogger<Gelato.Decorators.ImageProcessorDecorator>.Instance, factory);
    var originalImage = new MediaBrowser.Controller.Entities.ItemImageInfo { Path = originalPath, Type = MediaBrowser.Model.Entities.ImageType.Primary };
    var options = new MediaBrowser.Controller.Drawing.ImageProcessingOptions { Item = new MediaBrowser.Controller.Entities.Movies.Movie(), Image = originalImage, MaxWidth = 300 };
    handler.Response = _ => "image bytes";
    await decorator.ProcessImage(options);
    Check(handler.Count("image.tmdb.org/t/p/w342/poster.jpg") == 1 && handler.Count("image.tmdb.org/t/p/original/poster.jpg") == 0, "native thumbnail requests download small variant only");
    Check(ReferenceEquals(options.Image, originalImage) && !File.Exists(originalPath) && processedPath != originalPath, "native thumbnail does not replace full-size image metadata");
    await decorator.ProcessImage(options);
    Check(handler.Count("image.tmdb.org/t/p/w342/poster.jpg") == 1, "native thumbnail cache avoids repeated downloads");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"{count} checks passed.");

sealed class FakeFactory(FakeHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
sealed class FakeHandler : HttpMessageHandler
{
    public ConcurrentDictionary<string, int> Requests { get; } = new();
    public Func<Uri, string> Response { get; set; } = _ => "{}";
    public int Count(string key) => Requests.GetValueOrDefault(key);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        Requests.AddOrUpdate(uri.Host + uri.AbsolutePath, 1, (_, value) => value + 1);
        await Task.Delay(15, ct);
        return new(HttpStatusCode.OK) { Content = new StringContent(Response(uri), Encoding.UTF8, "application/json") };
    }
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
}

public class Proxy<T> : System.Reflection.DispatchProxy where T : class
{
    public Func<System.Reflection.MethodInfo?, object?[]?, object?> Call = null!;
    public static T Make(Func<System.Reflection.MethodInfo?, object?[]?, object?> call)
    {
        var instance = Create<T, Proxy<T>>();
        ((Proxy<T>)(object)instance).Call = call;
        return instance;
    }
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => Call(method, args);
}
