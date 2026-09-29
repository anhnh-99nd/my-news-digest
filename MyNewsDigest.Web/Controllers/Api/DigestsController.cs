using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Services;

namespace MyNewsDigest.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/digests")]
public class DigestsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 30) =>
        Ok(await db.Digests.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(d => new { d.Id, d.LocalDate, d.Slot, d.IsManual, d.PeriodStart, d.CreatedAt, ItemCount = d.Items.Count })
            .ToListAsync());

    /// <summary>
    /// Chi tiết bản tin. Sự kiện còn trong DB thì trả dữ liệu hiện tại (kèm các bài);
    /// đã bị xóa theo hạn lưu thì trả bản chụp lúc chốt.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var digest = await db.Digests.AsNoTracking()
            .Include(d => d.Items).ThenInclude(i => i.Cluster!).ThenInclude(c => c.Articles).ThenInclude(a => a.Source)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id);
        if (digest is null) return NotFound();

        var now = DateTime.UtcNow;
        return Ok(new
        {
            digest.Id, digest.LocalDate, digest.Slot, digest.IsManual, digest.PeriodStart, digest.CreatedAt,
            items = digest.Items.OrderBy(i => i.Rank).Select(i => new
            {
                i.Rank,
                i.Title,
                i.Category,
                CategoryLabel = Categories.Label(i.Category),
                i.Score,
                i.SourceCount,
                i.Url,
                cluster = i.Cluster is null ? null : ClusterDto.From(i.Cluster, now),
            }),
        });
    }

    /// <summary>Chốt bản tin ngay (không đợi khung giờ).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromServices] DigestService digests, CancellationToken ct)
    {
        var digest = await digests.CreateAsync("manual", isManual: true, ct);
        return Ok(new { digest.Id });
    }
}
