using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Logging;

namespace Gelato.Config;

public class PluginConfiguration : BasePluginConfiguration
{
    public string MoviePath { get; set; } = Path.Combine(Path.GetTempPath(), "gelato", "movies");
    public string SeriesPath { get; set; } = Path.Combine(Path.GetTempPath(), "gelato", "series");
    public int StreamTTL { get; set; } = 3600;
    public int CatalogMaxItems { get; set; } = 100;
    public string Url { get; set; } = "";
    public string MetadataUrl { get; set; } = "";
    public List<string> AddonUrls { get; set; } = [];

    public bool EnableMixed { get; set; } = false;
    public bool ExtendLocalSeriesTrees { get; set; } = false;
    public bool FilterUnreleased { get; set; } = false;
    public int FilterUnreleasedBufferDays { get; set; } = 0;
    public bool DisableSourceCount { get; set; } = true;
    public bool P2PEnabled { get; set; } = false;
    public int P2PDLSpeed { get; set; } = 0;
    public int P2PULSpeed { get; set; } = 0;
    public string FFmpegAnalyzeDuration { get; set; } = "5M";
    public string FFmpegProbeSize { get; set; } = "40M";
    public bool CreateCollections { get; set; } = false;
    public int MaxCollectionItems { get; set; } = 100;
    public bool DisableSearch { get; set; } = false;
    public bool EnableJavaScriptInjection { get; set; } = false;
    public bool LazyImages { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int ManifestRefreshSeconds { get; set; } = 300;
    public int CatalogRefreshSeconds { get; set; } = 300;
    public bool EnableHomeRows { get; set; } = false;
    public List<CatalogConfig> Catalogs { get; set; } = [];
    public List<UserConfig> UserConfigs { get; set; } = [];

    public string GetBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
            throw new InvalidOperationException("Gelato Url not configured.");

        return NormalizeUrl(Url);
    }

    public static string NormalizeUrl(string url)
    {
        var value = url.Trim().TrimEnd('/');
        if (value.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value[10..];
        if (value.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            value = value[..^14];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use an HTTP(S) addon base or manifest URL without query or fragment.");
        return value;
    }

    [JsonIgnore]
    [XmlIgnore]
    public GelatoStremioProvider? Stremio;

    [JsonIgnore]
    [XmlIgnore]
    public Folder? MovieFolder;

    [JsonIgnore]
    [XmlIgnore]
    public Folder? SeriesFolder;

    public PluginConfiguration GetEffectiveConfig(Guid userId)
    {
        var userConfig = UserConfigs.FirstOrDefault(u => u.UserId == userId);
        return userConfig is null ? this : userConfig.ApplyOverrides(this);
    }
}

public class UserConfig
{
    public Guid UserId { get; set; }
    public string Url { get; set; } = "";
    public string MetadataUrl { get; set; } = "";
    public List<string> AddonUrls { get; set; } = [];

    public string MoviePath { get; set; } = "";
    public string SeriesPath { get; set; } = "";
    public bool DisableSearch { get; set; } = false;

    /// <summary>
    /// Apply user overrides to base configuration - replaces all overridable fields
    /// </summary>
    public PluginConfiguration ApplyOverrides(PluginConfiguration baseConfig)
    {
        return new PluginConfiguration
        {
            // User overridable fields - all required, no fallback to baseConfig
            Url = Url,
            MetadataUrl = string.IsNullOrWhiteSpace(MetadataUrl) ? baseConfig.MetadataUrl : MetadataUrl,
            AddonUrls = AddonUrls,
            LazyImages = baseConfig.LazyImages,
            EnableJavaScriptInjection = baseConfig.EnableJavaScriptInjection,
            EnableHomeRows = baseConfig.EnableHomeRows,
            CatalogRefreshSeconds = baseConfig.CatalogRefreshSeconds,
            ManifestRefreshSeconds = baseConfig.ManifestRefreshSeconds,
            RequestTimeoutSeconds = baseConfig.RequestTimeoutSeconds,
            Catalogs = baseConfig.Catalogs,
            MoviePath = MoviePath,
            SeriesPath = SeriesPath,
            DisableSearch = DisableSearch,

            // All other fields from base config
            StreamTTL = baseConfig.StreamTTL,
            CatalogMaxItems = baseConfig.CatalogMaxItems,
            EnableMixed = baseConfig.EnableMixed,
            ExtendLocalSeriesTrees = baseConfig.ExtendLocalSeriesTrees,
            FilterUnreleased = baseConfig.FilterUnreleased,
            FilterUnreleasedBufferDays = baseConfig.FilterUnreleasedBufferDays,
            DisableSourceCount = baseConfig.DisableSourceCount,
            P2PEnabled = baseConfig.P2PEnabled,
            P2PDLSpeed = baseConfig.P2PDLSpeed,
            P2PULSpeed = baseConfig.P2PULSpeed,
            FFmpegAnalyzeDuration = baseConfig.FFmpegAnalyzeDuration,
            FFmpegProbeSize = baseConfig.FFmpegProbeSize,
            CreateCollections = baseConfig.CreateCollections,
            MaxCollectionItems = baseConfig.MaxCollectionItems,
            UserConfigs = baseConfig.UserConfigs,
        };
    }
}

public class GelatoStremioProviderFactory(IHttpClientFactory http, ILoggerFactory log)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        GelatoStremioProvider
    > _cache = new(StringComparer.Ordinal);

    public GelatoStremioProvider Create(Guid userId)
    {
        var cfg = GelatoPlugin.Instance!.Configuration.GetEffectiveConfig(userId);
        return Create(cfg);
    }

    public GelatoStremioProvider Create(PluginConfiguration cfg)
    {
        var urls = new[] { cfg.GetBaseUrl() }.Concat(cfg.AddonUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(PluginConfiguration.NormalizeUrl)).Distinct(StringComparer.Ordinal).ToArray();
        var metadata = string.IsNullOrWhiteSpace(cfg.MetadataUrl) ? null : PluginConfiguration.NormalizeUrl(cfg.MetadataUrl);
        var key = System.Text.Json.JsonSerializer.Serialize(new { urls, metadata, cfg.ManifestRefreshSeconds, cfg.RequestTimeoutSeconds });
        return _cache.GetOrAdd(key, _ =>
        {
            GelatoStremioProvider Endpoint(string url) => new(url, http, log.CreateLogger<GelatoStremioProvider>(), manifestRefreshSeconds: cfg.ManifestRefreshSeconds, requestTimeoutSeconds: cfg.RequestTimeoutSeconds);
            return new GelatoStremioProvider(urls[0], http, log.CreateLogger<GelatoStremioProvider>(),
                urls.Skip(1).Select(Endpoint).ToArray(), metadata is null ? null : Endpoint(metadata), cfg.ManifestRefreshSeconds, cfg.RequestTimeoutSeconds);
        });
    }

    public void ClearCache() => _cache.Clear();
}

public class CatalogConfig
{
    public string Source { get; set; } = "";
    public bool ShowOnHome { get; set; } = true;

    public string Id { get; set; } = "";
    public string Type { get; set; } = "movie";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = false;

    /// <summary>0 means "use global CatalogMaxItems".</summary>
    public int MaxItems { get; set; } = 0;
    public bool CreateCollection { get; set; } = false;
    public string Url { get; set; } = "";
}
