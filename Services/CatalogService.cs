using Gelato.Config;

namespace Gelato.Services;

public class CatalogService(GelatoStremioProviderFactory stremioFactory)
{
    public async Task<List<CatalogConfig>> GetCatalogsAsync(Guid userId)
    {
        var config = GelatoPlugin.Instance!.Configuration;
        var providers = stremioFactory.Create(userId).CatalogProviders.ToArray();
        var discovered = await Task.WhenAll(providers.Select(async provider =>
        {
            var manifest = await provider.GetManifestAsync();
            return (manifest?.Catalogs ?? []).Where(c => c.IsImportable() && c.Type is "movie" or "series").Select(c =>
            {
                var saved = config.Catalogs.FirstOrDefault(s => s.Id == c.Id && s.Type == c.Type && s.Source == provider.SourceKey)
                    ?? (provider == providers[0] ? config.Catalogs.FirstOrDefault(s => s.Id == c.Id && s.Type == c.Type && s.Source == "") : null);
                return new CatalogConfig
                {
                    Source = provider.SourceKey,
                    Id = c.Id,
                    Type = c.Type,
                    Name = c.Name,
                    Enabled = saved?.Enabled ?? false,
                    MaxItems = saved?.MaxItems ?? 0,
                    CreateCollection = saved?.CreateCollection ?? false,
                    ShowOnHome = saved?.ShowOnHome ?? true
                };
            }).ToList();
        }));
        // Discovery is read-only: dynamic manifests must never overwrite saved preferences.
        return discovered.SelectMany(c => c).ToList();
    }

    public void UpdateCatalogConfig(CatalogConfig updated)
    {
        var config = GelatoPlugin.Instance!.Configuration;
        var existing = config.Catalogs.FindIndex(c => c.Id == updated.Id && c.Type == updated.Type && c.Source == updated.Source);
        if (existing >= 0) config.Catalogs[existing] = updated;
        else config.Catalogs.Add(updated);
        GelatoPlugin.Instance.SaveConfiguration();
    }

    public CatalogConfig? GetCatalogConfig(string id, string type, string? source = null) =>
        GelatoPlugin.Instance!.Configuration.Catalogs.FirstOrDefault(c => c.Id == id && c.Type == type && (source is null || c.Source == source));
}
