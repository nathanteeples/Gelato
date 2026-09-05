using Gelato.Config;

namespace Gelato.Services;

/// <summary>Bounded, coalesced preview snapshots. No metadata hydration or library writes.</summary>
public sealed class CatalogPreviewService(GelatoStremioProviderFactory factory)
{
    private readonly PreviewCache<IReadOnlyList<StremioMeta>> _cache = new();

    public async Task<IReadOnlyList<StremioMeta>> GetAsync(Guid userId, CatalogConfig catalog, CancellationToken ct)
    {
        var cfg = GelatoPlugin.Instance!.Configuration.GetEffectiveConfig(userId);
        var provider = factory.Create(cfg).CatalogProviders.FirstOrDefault(p => p.SourceKey == catalog.Source)
            ?? throw new InvalidOperationException("Catalogue source is no longer configured.");
        // Name is part of the snapshot version: Watchly reuses slot IDs for new recommendations.
        var key = System.Text.Json.JsonSerializer.Serialize(new { userId, provider.SourceKey, catalog.Type, catalog.Id, catalog.Name, cfg.CatalogRefreshSeconds });
        return await _cache.GetAsync(key, TimeSpan.FromSeconds(Math.Clamp(cfg.CatalogRefreshSeconds, 30, 86400)), async () =>
            (await provider.GetCatalogMetasAsync(catalog.Id, catalog.Type))
                .Where(m => m.Type is StremioMediaType.Movie or StremioMediaType.Series && !string.IsNullOrWhiteSpace(m.Id))
                .DistinctBy(m => (m.Type, m.Id)).Take(40).ToArray(), ct);
    }

    public static string? Thumbnail(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        // Only rewrite a documented image service; arbitrary addon artwork URLs are opaque.
        if (uri.Host == "image.tmdb.org" && uri.AbsolutePath.StartsWith("/t/p/", StringComparison.Ordinal))
        {
            var parts = uri.AbsolutePath.Split('/');
            if (parts.Length >= 5) return $"{uri.Scheme}://{uri.Host}/t/p/w342/{string.Join('/', parts.Skip(4))}{uri.Query}";
        }
        return url;
    }
}
