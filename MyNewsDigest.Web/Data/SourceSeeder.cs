using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Data;

/// <summary>
/// Thêm các nguồn mặc định trong appsettings (mục "Sources") nếu DB chưa có.
/// Nguồn đã có thì không đụng tới: người dùng quản lý nguồn trên web.
/// </summary>
public static class SourceSeeder
{
    public static void Seed(AppDbContext db, IConfiguration config)
    {
        var configs = config.GetSection("Sources").Get<List<SourceConfig>>() ?? [];
        var existingKeys = db.Sources.Select(s => s.Key).ToHashSet();

        foreach (var cfg in configs.Where(c => !string.IsNullOrWhiteSpace(c.Key) && !existingKeys.Contains(c.Key)))
        {
            db.Sources.Add(new Source
            {
                Key = cfg.Key,
                Name = cfg.Name,
                FeedUrl = cfg.FeedUrl,
                IsInternational = cfg.IsInternational,
                Enabled = cfg.Enabled && !string.IsNullOrWhiteSpace(cfg.FeedUrl),
            });
        }
        db.SaveChanges();
    }
}
