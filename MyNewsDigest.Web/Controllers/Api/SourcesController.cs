using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Services;

namespace MyNewsDigest.Web.Controllers.Api;

/// <summary>Quản lý nguồn tin: xem trạng thái, thêm/sửa/xóa, thử link RSS.</summary>
[ApiController]
[Authorize]
[Route("api/sources")]
public class SourcesController(AppDbContext db) : ControllerBase
{
    public record SourceRequest(string Name, string FeedUrl, bool IsInternational, bool Enabled);
    public record TestRequest(string FeedUrl);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var counts = await db.Articles.GroupBy(a => a.SourceId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var sources = await db.Sources.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        return Ok(sources.Select(s => new
        {
            s.Id, s.Key, s.Name, s.FeedUrl, s.IsInternational, s.Enabled,
            s.LastFetchedAt, s.LastItemCount, s.LastError,
            Logo = TextHelpers.LogoUrl(s.FeedUrl),
            ArticleCount = counts.GetValueOrDefault(s.Id),
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(SourceRequest req)
    {
        if (Validate(req) is { } error) return BadRequest(new { message = error });

        var baseKey = TextHelpers.Slug(req.Name);
        var key = baseKey;
        for (var i = 2; await db.Sources.AnyAsync(s => s.Key == key); i++) key = $"{baseKey}-{i}";

        var source = new Source { Key = key };
        Apply(source, req);
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return Ok(source);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SourceRequest req)
    {
        if (Validate(req) is { } error) return BadRequest(new { message = error });
        var source = await db.Sources.FindAsync(id);
        if (source is null) return NotFound();
        Apply(source, req);
        await db.SaveChangesAsync();
        return Ok(source);
    }

    /// <summary>Xóa nguồn kèm toàn bộ bài của nguồn đó.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var source = await db.Sources.FindAsync(id);
        if (source is null) return NotFound();
        await db.Articles.Where(a => a.SourceId == id).ExecuteDeleteAsync();
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        await db.Clusters.Where(c => !c.IsSaved && !c.Articles.Any()).ExecuteDeleteAsync();
        return NoContent();
    }

    /// <summary>Thử tải link RSS trước khi lưu.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(TestRequest req, [FromServices] FeedFetcher fetcher, CancellationToken ct)
    {
        var urls = TextHelpers.FeedUrls(req.FeedUrl);
        if (urls.Length == 0 || urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out _)))
            return BadRequest(new { message = "Link RSS không hợp lệ" });

        var items = new List<FeedItem>();
        var errors = new List<string>();
        foreach (var url in urls)
        {
            try { items.AddRange(await fetcher.FetchAsync(url, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                errors.Add($"{url}: {ex.Message}");
            }
        }
        // Báo luôn bài mới nhất để phát hiện feed đã ngừng cập nhật
        var newest = items.Max(i => i.PublishedAt);
        return Ok(new
        {
            ok = items.Count > 0,
            count = items.Count,
            newest,
            samples = items.OrderByDescending(i => i.PublishedAt).Take(3).Select(i => i.Title),
            error = errors.Count > 0 ? string.Join(" | ", errors) : null,
        });
    }

    private static string? Validate(SourceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Tên nguồn không được trống";
        var bad = TextHelpers.FeedUrls(req.FeedUrl).FirstOrDefault(u => !Uri.TryCreate(u, UriKind.Absolute, out _));
        if (bad != null) return $"Link RSS không hợp lệ: {bad}";
        return null;
    }

    private static void Apply(Source s, SourceRequest req)
    {
        s.Name = req.Name.Trim();
        s.FeedUrl = string.Join('\n', TextHelpers.FeedUrls(req.FeedUrl));
        s.IsInternational = req.IsInternational;
        s.Enabled = req.Enabled && s.FeedUrl.Length > 0;
    }
}
