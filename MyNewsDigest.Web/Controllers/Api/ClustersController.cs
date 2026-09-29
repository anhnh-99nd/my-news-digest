using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;

namespace MyNewsDigest.Web.Controllers.Api;

/// <summary>Đánh dấu đã đọc / đọc sau cho từng sự kiện.</summary>
[ApiController]
[Authorize]
[Route("api/clusters")]
public class ClustersController(AppDbContext db) : ControllerBase
{
    public record IdsRequest(List<int> Ids);

    [HttpPost("{id:int}/read")]
    public Task<IActionResult> MarkRead(int id) => Update(id, c => c.ReadAt = DateTime.UtcNow);

    [HttpDelete("{id:int}/read")]
    public Task<IActionResult> MarkUnread(int id) => Update(id, c => c.ReadAt = null);

    /// <summary>Đánh dấu đã đọc nhiều sự kiện một lúc (vd. "đọc hết" trang hiện tại).</summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkManyRead(IdsRequest req)
    {
        var now = DateTime.UtcNow;
        var count = await db.Clusters.Where(c => req.Ids.Contains(c.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReadAt, now));
        return Ok(new { updated = count });
    }

    [HttpPost("{id:int}/save")]
    public Task<IActionResult> Save(int id) => Update(id, c =>
    {
        c.IsSaved = true;
        c.SavedAt = DateTime.UtcNow;
    });

    [HttpDelete("{id:int}/save")]
    public Task<IActionResult> Unsave(int id) => Update(id, c =>
    {
        c.IsSaved = false;
        c.SavedAt = null;
    });

    private async Task<IActionResult> Update(int id, Action<Models.Cluster> change)
    {
        var cluster = await db.Clusters.FindAsync(id);
        if (cluster is null) return NotFound();
        change(cluster);
        await db.SaveChangesAsync();
        return Ok(new { cluster.Id, cluster.IsRead, cluster.HasUpdate, cluster.IsSaved });
    }
}
