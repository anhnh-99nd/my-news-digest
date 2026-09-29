using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;

namespace MyNewsDigest.Web.Services;

/// <summary>Chốt bản tin tổng hợp: top N sự kiện hot nhất kể từ bản tin trước.</summary>
public class DigestService(AppDbContext db, SettingsService settingsService, ILogger<DigestService> log)
{
    /// <summary>Khoảng thời gian một bản tin bao phủ: từ bản tin trước, tối thiểu 6 giờ, tối đa 24 giờ.</summary>
    private static readonly TimeSpan MinPeriod = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxPeriod = TimeSpan.FromHours(24);

    /// <summary>
    /// Gọi sau mỗi lượt cập nhật (không có job nền): nếu đã qua một khung giờ trong ngày mà chưa có
    /// bản tin cho khung đó thì tạo. Chỉ tạo cho khung gần nhất đã qua, không tạo bù các khung trước.
    /// </summary>
    public async Task<Digest?> CreateScheduledIfDueAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var local = VnTime.ToLocal(DateTime.UtcNow);
        var now = TimeOnly.FromDateTime(local);
        var due = settings.DigestTimes
            .Select(t => TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture))
            .Where(t => t <= now)
            .Select(t => t.ToString("HH:mm"))
            .LastOrDefault();
        if (due is null) return null;

        var date = local.ToString("yyyy-MM-dd");
        if (await db.Digests.AnyAsync(d => d.LocalDate == date && d.Slot == due, ct)) return null;
        return await CreateAsync(due, isManual: false, ct);
    }

    public async Task<Digest> CreateAsync(string slot, bool isManual, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var local = VnTime.ToLocal(nowUtc);

        var last = await db.Digests.OrderByDescending(d => d.CreatedAt).Select(d => (DateTime?)d.CreatedAt).FirstOrDefaultAsync(ct);
        var start = last ?? nowUtc - MaxPeriod;
        if (nowUtc - start < MinPeriod) start = nowUtc - MinPeriod;
        if (nowUtc - start > MaxPeriod) start = nowUtc - MaxPeriod;

        var clusters = await db.Clusters
            .Where(c => c.LastUpdatedAt >= start)
            .Include(c => c.Articles).ThenInclude(a => a.Source)
            .AsSplitQuery()
            .ToListAsync(ct);

        var top = clusters
            .OrderByDescending(HotScore.Display)
            .ThenByDescending(c => c.SourceCount)
            .ThenByDescending(c => c.LastUpdatedAt)
            .Take(settings.DigestSize)
            .ToList();

        var digest = new Digest
        {
            LocalDate = local.ToString("yyyy-MM-dd"),
            Slot = isManual ? $"manual-{local:HHmmss}" : slot,
            IsManual = isManual,
            PeriodStart = start,
            CreatedAt = nowUtc,
            Items = top.Select((c, i) => new DigestItem
            {
                Rank = i + 1,
                ClusterId = c.Id,
                Title = TextHelpers.ClusterTitle(c),
                Category = c.Category,
                Score = HotScore.Display(c),
                SourceCount = c.SourceCount,
                Url = c.Articles.OrderBy(a => a.PublishedAt).FirstOrDefault()?.Url,
            }).ToList(),
        };
        db.Digests.Add(digest);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Đã tạo bản tin {Date} {Slot} với {Count} sự kiện", digest.LocalDate, digest.Slot, digest.Items.Count);
        return digest;
    }
}
