using Gelato.Config;
using Gelato.ScheduledTasks;
using Gelato.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Gelato.Controllers;

[ApiController]
[Route("gelato/catalogs")]
[Authorize]
public class CatalogController(
    ILogger<CatalogController> logger,
    CatalogService catalogService,
    CatalogImportService importService,
    ITaskManager taskManager,
    CatalogPreviewService previews,
    GelatoManager manager
) : ControllerBase
{
    private Guid? CurrentUserId => Guid.TryParse(User.Claims.FirstOrDefault(c => c.Type is "Jellyfin-UserId" or "UserId")?.Value, out var id) && id != Guid.Empty ? id : null;

    [HttpGet("home")]
    public async Task<IActionResult> Home()
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        if (!GelatoPlugin.Instance!.Configuration.EnableHomeRows) return Ok(Array.Empty<object>());
        var catalogs = await catalogService.GetCatalogsAsync(userId);
        return Ok(catalogs.Where(c => c.ShowOnHome).Select(c => new { c.Source, c.Id, c.Type, c.Name }));
    }

    [HttpGet("home/{source}/{type}/{id}")]
    public async Task<IActionResult> Preview(string source, string type, string id)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        if (!GelatoPlugin.Instance!.Configuration.EnableHomeRows) return NotFound();
        var catalog = (await catalogService.GetCatalogsAsync(userId)).FirstOrDefault(c => c.Source == source && c.Type == type && c.Id == id && c.ShowOnHome);
        if (catalog is null) return NotFound();
        var items = await previews.GetAsync(userId, catalog, HttpContext.RequestAborted);
        return Ok(items.Select(meta => new { meta.Id, Type = meta.Type.ToString().ToLowerInvariant(), Name = meta.GetName(), Poster = CatalogPreviewService.Thumbnail(meta.Poster), Year = meta.GetYear() }));
    }

    [HttpPost("home/{source}/{type}/{id}/open/{itemId}")]
    public async Task<IActionResult> Open(string source, string type, string id, string itemId)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        if (!GelatoPlugin.Instance!.Configuration.EnableHomeRows) return NotFound();
        var catalog = (await catalogService.GetCatalogsAsync(userId)).FirstOrDefault(c => c.Source == source && c.Type == type && c.Id == id && c.ShowOnHome);
        if (catalog is null) return NotFound();
        var meta = (await previews.GetAsync(userId, catalog, HttpContext.RequestAborted)).FirstOrDefault(m => m.Id == itemId);
        if (meta is null || manager.IntoBaseItem(meta) is not { } item) return NotFound();
        manager.SaveStremioMeta(item.Id, meta);
        return Ok(new { item.Id });
    }

    [Authorize(Policy = "RequiresElevation")]
    [HttpGet]
    public async Task<ActionResult<List<CatalogConfig>>> GetCatalogs()
    {
        return await catalogService.GetCatalogsAsync(Guid.Empty);
    }

    [Authorize(Policy = "RequiresElevation")]
    [HttpPost("{id}/{type}/config")]
    public ActionResult UpdateConfig(
        [FromRoute] string id,
        [FromRoute] string type,
        [FromBody] CatalogConfig config
    )
    {
        if (config.Id != id || config.Type != type)
        {
            return BadRequest("ID/Type mismatch");
        }

        catalogService.UpdateCatalogConfig(config);
        return Ok();
    }

    [Authorize(Policy = "RequiresElevation")]
    [HttpPost("{id}/{type}/import")]
    public Task<ActionResult> TriggerImport([FromRoute] string id, [FromRoute] string type, [FromQuery] string? source = null)
    {
        logger.LogInformation("Manual import triggered for {Id} {Type}", id, type);

        _ = Task.Run(async () =>
        {
            try
            {
                await importService.ImportCatalogAsync(id, type, CancellationToken.None, source: source);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in manual import for {Id}", id);
            }
        });

        return Task.FromResult<ActionResult>(Accepted());
    }

    [Authorize(Policy = "RequiresElevation")]
    [HttpPost("import-all")]
    public ActionResult ImportAll()
    {
        logger.LogInformation("Manual import triggered for all enabled catalogs");

        _ = Task.Run(() =>
        {
            try
            {
                taskManager.CancelIfRunningAndQueue<GelatoCatalogItemsSyncTask>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in manual import for all enabled catalogs");
            }

            return Task.CompletedTask;
        });

        return Accepted();
    }
}
