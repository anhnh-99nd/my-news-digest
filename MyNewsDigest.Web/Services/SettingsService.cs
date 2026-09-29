using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Services;

/// <summary>Cài đặt người dùng chỉnh trên web (lưu DB). Giá trị mặc định lấy từ appsettings.</summary>
public class AppSettings
{
    public int RetentionDays { get; set; } = 30;
    /// <summary>Ngưỡng điểm mặc định của bộ lọc.</summary>
    public double DefaultMinScore { get; set; }
    /// <summary>Thứ tự nhà cung cấp AI: "cloudflare", "gemini".</summary>
    public List<string> AiProviders { get; set; } = ["cloudflare", "gemini"];
    /// <summary>Các giờ chốt bản tin (giờ Việt Nam, HH:mm).</summary>
    public List<string> DigestTimes { get; set; } = ["07:00", "19:00"];
    public int DigestSize { get; set; } = 15;
    /// <summary>"list" | "digest" | "both".</summary>
    public string ViewMode { get; set; } = "both";
    public bool HideRead { get; set; }
}

public class SettingsService(AppDbContext db, IOptions<RefreshOptions> refresh, IOptions<AiOptions> ai)
{
    private const string Key = "app";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var settings = row is null ? null : JsonSerializer.Deserialize<AppSettings>(row.Value, Json);
        return Normalize(settings ?? Defaults());
    }

    public async Task<AppSettings> SaveAsync(AppSettings input, CancellationToken ct = default)
    {
        var settings = Normalize(input);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null) db.Settings.Add(row = new AppSetting { Key = Key });
        row.Value = JsonSerializer.Serialize(settings, Json);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private AppSettings Defaults() => new()
    {
        RetentionDays = refresh.Value.RetentionDays,
        AiProviders = ai.Value.Providers is { Count: > 0 } p ? [.. p] : ["cloudflare", "gemini"],
    };

    /// <summary>Kẹp giá trị về khoảng hợp lệ, chuẩn hóa giờ bản tin.</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        s.RetentionDays = Math.Clamp(s.RetentionDays, 1, 365);
        s.DefaultMinScore = Math.Clamp(s.DefaultMinScore, 0, 10);
        s.DigestSize = Math.Clamp(s.DigestSize, 3, 50);
        s.ViewMode = s.ViewMode is "list" or "digest" or "both" ? s.ViewMode : "both";

        s.AiProviders = (s.AiProviders ?? [])
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p is "cloudflare" or "gemini")
            .Distinct()
            .ToList();
        // Luôn giữ nhà cung cấp còn lại làm dự phòng ở cuối danh sách
        foreach (var p in new[] { "cloudflare", "gemini" })
            if (!s.AiProviders.Contains(p)) s.AiProviders.Add(p);

        s.DigestTimes = (s.DigestTimes ?? [])
            .Select(t => TimeOnly.TryParse(t, CultureInfo.InvariantCulture, out var time) ? time.ToString("HH:mm") : null)
            .OfType<string>()
            .Distinct()
            .Order()
            .ToList();
        return s;
    }
}
