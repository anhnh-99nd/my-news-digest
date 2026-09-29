using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Services;

namespace MyNewsDigest.Web.Controllers.Api;

public record ArticleDto(
    string Title, string Url, string SourceKey, string SourceName, string? SourceLogo,
    DateTime PublishedAt, bool IsInternational, string? ImageUrl);

public record ClusterDto(
    int Id, string Title, string? ImageUrl, string Category, string CategoryLabel,
    double Score, double Rank, string? AiReason, int SourceCount, int ArticleCount, bool Trend, bool IsScored,
    bool IsRead, bool HasUpdate, bool IsSaved,
    DateTime FirstSeenAt, DateTime LastUpdatedAt, List<ArticleDto> Articles)
{
    public static ClusterDto From(Cluster c, DateTime now)
    {
        var articles = c.Articles
            .OrderBy(a => a.PublishedAt)
            .Select(a => new ArticleDto(a.Title, a.Url, a.Source.Key, a.Source.Name, TextHelpers.LogoUrl(a.Source.FeedUrl),
                a.PublishedAt, a.Source.IsInternational, string.IsNullOrEmpty(a.ImageUrl) ? null : a.ImageUrl))
            .ToList();
        return new ClusterDto(
            c.Id, TextHelpers.ClusterTitle(c), TextHelpers.ClusterImage(c), c.Category, Categories.Label(c.Category),
            HotScore.Display(c), Math.Round(HotScore.Rank(c, now), 3), c.AiReason,
            c.SourceCount, c.ArticleCount, c.TrendMatch, c.AiScore.HasValue,
            c.IsRead, c.HasUpdate, c.IsSaved,
            c.FirstSeenAt, c.LastUpdatedAt, articles);
    }
}

[ApiController]
[Authorize]
[Route("api/news")]
public class NewsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Danh sách sự kiện theo độ hot (giảm dần theo thời gian).
    /// view: all | unread | saved. scope: all | domestic | international.
    /// categories, sources: danh sách mã, phân tách bằng dấu phẩy. q: từ khóa (không phân biệt dấu).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int hours = 24,
        [FromQuery] string? categories = null,
        [FromQuery] string? sources = null,
        [FromQuery] string scope = "all",
        [FromQuery] double minScore = 0,
        [FromQuery] string? q = null,
        [FromQuery] string view = "all",
        [FromQuery] int limit = 100)
    {
        var now = DateTime.UtcNow;
        var query = db.Clusters.AsNoTracking();
        if (view == "saved")
            query = query.Where(c => c.IsSaved);
        else
            query = query.Where(c => c.LastUpdatedAt >= now.AddHours(-Math.Clamp(hours, 1, 24 * 365)));

        var clusters = await query
            .Include(c => c.Articles).ThenInclude(a => a.Source)
            .AsSplitQuery()
            .ToListAsync();

        var categorySet = SplitSet(categories);
        var sourceSet = SplitSet(sources);
        var keyword = string.IsNullOrWhiteSpace(q) ? null : TextHelpers.Fold(q.Trim());

        IEnumerable<Cluster> filtered = clusters;
        if (categorySet != null) filtered = filtered.Where(c => categorySet.Contains(c.Category));
        if (sourceSet != null) filtered = filtered.Where(c => c.Articles.Any(a => sourceSet.Contains(a.Source.Key)));
        // Phạm vi theo NỘI DUNG tin, không theo quốc tịch báo: báo Việt đưa tin Iran vẫn là tin quốc tế
        if (scope == "domestic") filtered = filtered.Where(c => !IsInternationalNews(c));
        else if (scope == "international") filtered = filtered.Where(IsInternationalNews);
        if (view == "unread") filtered = filtered.Where(c => !c.IsRead);
        if (keyword != null)
            filtered = filtered.Where(c => c.Articles.Any(a => TextHelpers.Fold(a.Title).Contains(keyword)));

        var items = filtered
            .Select(c => ClusterDto.From(c, now))
            .Where(c => c.Score >= minScore)
            .OrderByDescending(c => view == "saved" ? 0 : c.Rank)
            .ThenByDescending(c => c.LastUpdatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();

        return Ok(new { generatedAt = now, total = items.Count, items });
    }

    /// <summary>Dữ liệu cho bộ lọc: chuyên mục + nguồn tin.</summary>
    [HttpGet("meta")]
    public async Task<IActionResult> Meta() => Ok(new
    {
        categories = Categories.Labels.Select(kv => new { key = kv.Key, label = kv.Value }),
        sources = (await db.Sources.AsNoTracking().OrderBy(s => s.Id).ToListAsync())
            .Select(s => new { s.Key, s.Name, s.IsInternational, s.Enabled, Logo = TextHelpers.LogoUrl(s.FeedUrl) }),
    });

    /// <summary>
    /// Tin quốc tế theo AI (trường region). Cụm chưa được AI đánh giá: chuyên mục Thế giới hoặc chỉ báo nước ngoài đưa tin.
    /// </summary>
    private static bool IsInternationalNews(Cluster c) =>
        c.IsInternational ?? (c.Category == "the-gioi" || c.Articles.All(a => a.Source.IsInternational));

    private static HashSet<string>? SplitSet(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
}
