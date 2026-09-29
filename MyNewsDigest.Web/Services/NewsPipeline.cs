using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Services;

public record RefreshResult(int NewArticles, int NewClusters, int ScoredClusters, int FailedSources, List<string> Warnings)
{
    public string Summary =>
        $"Xong: {NewArticles} bài mới, {NewClusters} sự kiện mới, AI chấm {ScoredClusters} sự kiện" +
        (FailedSources > 0 ? $", {FailedSources} nguồn lỗi" : "");
}

/// <summary>
/// Một lượt cập nhật: tải RSS → lưu bài mới → embedding → gom cụm → Google Trends → Gemini chấm điểm/tóm tắt → dọn tin cũ.
/// </summary>
public partial class NewsPipeline(
    AppDbContext db,
    FeedFetcher feeds,
    ImageFetcher images,
    EmbeddingClient embeddings,
    CloudflareChatClient cloudflareChat,
    GeminiClient gemini,
    IOptionsMonitor<RefreshOptions> refreshOptions,
    IOptionsMonitor<AiOptions> aiOptions,
    SettingsService settingsService,
    IOptionsMonitor<TrendsOptions> trendsOptions,
    ILogger<NewsPipeline> log)
{
    private const double TitleJaccardThreshold = 0.5;

    private sealed record PoolItem(Cluster Cluster, float[]? Vector, HashSet<string> Tokens);

    public async Task<RefreshResult> RunAsync(Action<string, string> report, CancellationToken ct)
    {
        var opt = refreshOptions.CurrentValue;
        var settings = await settingsService.GetAsync(ct);
        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-opt.WindowHours);
        var warnings = new List<string>();

        // 1. Tải RSS song song
        var sources = await db.Sources.Where(s => s.Enabled).ToListAsync(ct);
        report("fetch", $"Đang tải tin từ {sources.Count} nguồn…");
        var fetched = new ConcurrentDictionary<int, List<FeedItem>>();
        var errors = new ConcurrentDictionary<int, string>();
        await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (source, token) =>
            {
                try
                {
                    // Nhiều link RSS: gộp bài; chỉ báo lỗi khi tất cả link đều lỗi
                    var items = new List<FeedItem>();
                    Exception? lastError = null;
                    foreach (var url in TextHelpers.FeedUrls(source.FeedUrl))
                    {
                        try { items.AddRange(await feeds.FetchAsync(url, token)); }
                        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested) { lastError = ex; }
                    }
                    if (items.Count == 0 && lastError != null) throw lastError;
                    fetched[source.Id] = items;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    errors[source.Id] = ex.Message;
                }
            });

        foreach (var s in sources)
        {
            s.LastFetchedAt = now;
            s.LastError = errors.GetValueOrDefault(s.Id);
            s.LastItemCount = fetched.TryGetValue(s.Id, out var list) ? list.Count : 0;
            if (s.LastError != null)
            {
                warnings.Add($"{s.Name}: {s.LastError}");
                log.LogWarning("Lỗi tải {Source}: {Error}", s.Name, s.LastError);
            }
        }

        // 2. Lọc bài mới (chưa có trong DB, còn trong khung thời gian)
        report("store", "Đang lọc bài mới…");
        var candidates = fetched
            .SelectMany(kv => kv.Value.Select(item => (SourceId: kv.Key, Item: item)))
            .Where(x => (x.Item.PublishedAt ?? now) >= windowStart)
            .DistinctBy(x => x.Item.Url)
            .ToList();
        var urls = candidates.Select(x => x.Item.Url).ToList();
        var existingUrls = (await db.Articles.Where(a => urls.Contains(a.Url)).Select(a => a.Url).ToListAsync(ct))
            .ToHashSet();

        // Bài cũ chưa có ảnh (lưu trước khi có tính năng ảnh) → bổ sung ảnh từ RSS
        var rssImages = candidates.Where(x => x.Item.ImageUrl != null)
            .ToDictionary(x => x.Item.Url, x => x.Item.ImageUrl!);
        var imageUrls = rssImages.Keys.Where(existingUrls.Contains).ToList();
        foreach (var old in await db.Articles.Where(a => imageUrls.Contains(a.Url) && (a.ImageUrl == null || a.ImageUrl == "")).ToListAsync(ct))
            old.ImageUrl = rssImages[old.Url];

        var newArticles = candidates
            .Where(x => !existingUrls.Contains(x.Item.Url))
            .Select(x => new Article
            {
                SourceId = x.SourceId,
                Title = x.Item.Title,
                Url = x.Item.Url,
                Description = x.Item.Description,
                ImageUrl = x.Item.ImageUrl,
                PublishedAt = x.Item.PublishedAt is { } p && p <= now ? p : now,
                FetchedAt = now,
            })
            .OrderBy(a => a.PublishedAt)
            .ToList();

        // 3. Embedding
        if (newArticles.Count > 0 && embeddings.IsConfigured)
        {
            report("embed", $"Đang phân tích {newArticles.Count} bài mới…");
            var texts = newArticles
                .Select(a => a.Description is null ? a.Title : $"{a.Title}. {Truncate(a.Description, 300)}")
                .ToList();
            var vectors = await embeddings.EmbedAsync(texts, ct);
            for (var i = 0; i < newArticles.Count; i++)
                if (vectors[i] is { } v) newArticles[i].Embedding = VectorMath.ToBytes(v);
            if (vectors.Any(v => v is null)) warnings.Add("Một số bài không lấy được embedding, dùng so khớp tiêu đề thay thế.");
        }
        else if (!embeddings.IsConfigured)
        {
            warnings.Add("Chưa cấu hình Cloudflare Workers AI, gom cụm bằng so khớp tiêu đề.");
        }

        // 4. Gom cụm
        report("cluster", "Đang gom các bài cùng sự kiện…");
        var (newClusterCount, clusterTitles) = await ClusterAsync(newArticles, windowStart, opt.SimilarityThreshold, ct);
        db.Articles.AddRange(newArticles);
        await db.SaveChangesAsync(ct);

        // 5. Google Trends
        await ApplyTrendsAsync(clusterTitles, warnings, ct);
        await db.SaveChangesAsync(ct);

        // 6. Gemini chấm điểm + tóm tắt
        var scored = await ScoreAsync(windowStart, settings.AiProviders, report, warnings, ct);

        // 7. Ảnh cho sự kiện chưa có ảnh nào (lấy og:image từ trang bài)
        await EnrichImagesAsync(windowStart, report, ct);

        // 8. Dọn tin cũ
        report("cleanup", "Đang dọn tin cũ…");
        var retentionCutoff = now.AddDays(-settings.RetentionDays);
        // Sự kiện "đọc sau" được giữ vĩnh viễn
        await db.Articles
            .Where(a => a.PublishedAt < retentionCutoff && (a.Cluster == null || !a.Cluster.IsSaved))
            .ExecuteDeleteAsync(ct);
        await db.Clusters.Where(c => !c.Articles.Any()).ExecuteDeleteAsync(ct);

        return new RefreshResult(newArticles.Count, newClusterCount, scored, errors.Count, warnings);
    }

    /// <summary>Gắn mỗi bài mới vào cụm gần nhất (cosine embedding, hoặc Jaccard tiêu đề), không có thì tạo cụm mới.</summary>
    private async Task<(int NewClusters, Dictionary<Cluster, List<string>> Titles)> ClusterAsync(
        List<Article> newArticles, DateTime windowStart, double threshold, CancellationToken ct)
    {
        var recentClusters = await db.Clusters.Where(c => c.LastUpdatedAt >= windowStart).ToListAsync(ct);
        var clusterIds = recentClusters.Select(c => c.Id).ToList();
        var recentArticles = await db.Articles
            .Where(a => a.ClusterId != null && clusterIds.Contains(a.ClusterId.Value))
            .Select(a => new { ClusterId = a.ClusterId!.Value, a.SourceId, a.Title, a.Embedding })
            .ToListAsync(ct);

        var byId = recentClusters.ToDictionary(c => c.Id);
        var pool = new List<PoolItem>();
        var sourceSets = recentClusters.ToDictionary(c => c, _ => new HashSet<int>());
        var titles = recentClusters.ToDictionary(c => c, _ => new List<string>());

        foreach (var a in recentArticles)
        {
            var cluster = byId[a.ClusterId];
            sourceSets[cluster].Add(a.SourceId);
            titles[cluster].Add(a.Title);
            pool.Add(new PoolItem(cluster, a.Embedding is null ? null : VectorMath.FromBytes(a.Embedding), Tokenize(a.Title)));
        }

        var created = 0;
        foreach (var article in newArticles)
        {
            var vector = article.Embedding is null ? null : VectorMath.FromBytes(article.Embedding);
            var tokens = Tokenize(article.Title);

            Cluster? best = null;
            double bestScore = 0;
            foreach (var p in pool)
            {
                bool match;
                double score;
                if (vector != null && p.Vector != null)
                {
                    score = VectorMath.Cosine(vector, p.Vector);
                    match = score >= threshold;
                }
                else
                {
                    score = Jaccard(tokens, p.Tokens);
                    match = score >= TitleJaccardThreshold;
                }
                if (match && score > bestScore)
                {
                    bestScore = score;
                    best = p.Cluster;
                }
            }

            if (best is null)
            {
                best = new Cluster
                {
                    Title = article.Title,
                    FirstSeenAt = article.PublishedAt,
                    LastUpdatedAt = article.PublishedAt,
                    LastChangedAt = article.FetchedAt,
                    NeedsScoring = true,
                };
                db.Clusters.Add(best);
                sourceSets[best] = [];
                titles[best] = [];
                created++;
            }

            article.Cluster = best;
            best.ArticleCount++;
            if (sourceSets[best].Add(article.SourceId))
            {
                best.SourceCount = sourceSets[best].Count;
                best.NeedsScoring = true; // thêm báo mới đưa tin → chấm lại
            }
            if (article.PublishedAt > best.LastUpdatedAt) best.LastUpdatedAt = article.PublishedAt;
            best.LastChangedAt = article.FetchedAt;
            titles[best].Add(article.Title);
            pool.Add(new PoolItem(best, vector, tokens));
        }

        return (created, titles);
    }

    private async Task ApplyTrendsAsync(Dictionary<Cluster, List<string>> clusterTitles, List<string> warnings, CancellationToken ct)
    {
        var t = trendsOptions.CurrentValue;
        if (!t.Enabled || string.IsNullOrWhiteSpace(t.FeedUrl)) return;

        List<string> trends;
        try
        {
            trends = (await feeds.FetchAsync(t.FeedUrl, ct))
                .Select(i => i.Title.Trim())
                .Where(k => k.Length >= 4)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Google Trends: {ex.Message}");
            return;
        }

        foreach (var (cluster, list) in clusterTitles)
            cluster.TrendMatch = trends.Any(k => list.Any(title => title.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<int> ScoreAsync(DateTime windowStart, List<string> providers, Action<string, string> report, List<string> warnings, CancellationToken ct)
    {
        var scorers = OrderedScorers(providers);
        if (scorers.Count == 0)
        {
            warnings.Add("Chưa cấu hình AI (Cloudflare Workers AI / Gemini), bỏ qua bước chấm điểm/tóm tắt.");
            return 0;
        }

        var toScore = await db.Clusters
            .Where(c => c.NeedsScoring && c.LastUpdatedAt >= windowStart)
            .OrderByDescending(c => c.SourceCount)
            .ThenByDescending(c => c.LastUpdatedAt)
            .Take(Math.Max(1, aiOptions.CurrentValue.MaxClustersPerRun))
            .Include(c => c.Articles).ThenInclude(a => a.Source)
            .AsSplitQuery()
            .ToListAsync(ct);
        if (toScore.Count == 0) return 0;

        var scored = 0;
        var provider = 0;
        var i = 0;
        while (i < toScore.Count && provider < scorers.Count)
        {
            var scorer = scorers[provider];
            var batch = toScore.Skip(i).Take(scorer.BatchSize).ToList();
            report("ai", $"AI ({scorer.Name}) đang chấm điểm và tóm tắt {i + batch.Count}/{toScore.Count} sự kiện…");

            var prompts = batch.Select(c => new ClusterPrompt(
                c.Id,
                c.Articles.Select(a => a.Source.Name).Distinct().ToList(),
                c.Articles.Select(a => a.Title).Distinct().Take(6).ToList(),
                c.Articles.Where(a => a.Description != null).Select(a => Truncate(a.Description!, 300)).Take(3).ToList()
            )).ToList();

            List<ClusterAiResult> results;
            try
            {
                results = await scorer.ScoreAsync(prompts, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Thường là hết quota: chuyển sang nhà cung cấp kế tiếp, chấm lại đúng lô này
                log.LogWarning(ex, "{Provider} lỗi ở lô {Index}", scorer.Name, i);
                warnings.Add($"{scorer.Name}: {ex.Message}");
                provider++;
                continue;
            }
            i += batch.Count;

            var byId = batch.ToDictionary(c => c.Id);
            foreach (var r in results)
            {
                if (!byId.TryGetValue(r.Id, out var c)) continue;
                c.Category = Categories.Normalize(r.Category);
                c.IsInternational = r.Region switch { "quoc-te" => true, "trong-nuoc" => false, _ => null };
                c.AiScore = Math.Clamp(r.Score, 1, 10);
                c.AiReason = r.Reason;
                c.NeedsScoring = false;
                c.ScoredAt = DateTime.UtcNow;
                scored++;
            }
            await db.SaveChangesAsync(ct);
        }
        return scored;
    }

    /// <summary>
    /// Sự kiện chưa bài nào có ảnh: tải trang bài đầu tiên (chưa thử) để lấy og:image.
    /// Giới hạn mỗi lượt để không làm chậm cập nhật; ưu tiên sự kiện nhiều báo, mới.
    /// </summary>
    private async Task EnrichImagesAsync(DateTime windowStart, Action<string, string> report, CancellationToken ct)
    {
        const int maxPerRun = 60;
        var targets = await db.Clusters
            .Where(c => c.LastUpdatedAt >= windowStart && !c.Articles.Any(a => a.ImageUrl != null && a.ImageUrl != ""))
            .OrderByDescending(c => c.SourceCount)
            .ThenByDescending(c => c.LastUpdatedAt)
            .Select(c => c.Articles.Where(a => a.ImageUrl == null).OrderBy(a => a.PublishedAt).FirstOrDefault())
            .Where(a => a != null)
            .Take(maxPerRun)
            .ToListAsync(ct);
        if (targets.Count == 0) return;

        report("images", $"Đang lấy ảnh cho {targets.Count} sự kiện…");
        var found = new ConcurrentDictionary<int, string>();
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (article, token) => found[article!.Id] = await images.FetchOgImageAsync(article.Url, token) ?? "");

        var ids = found.Keys.ToList();
        foreach (var article in await db.Articles.Where(a => ids.Contains(a.Id)).ToListAsync(ct))
            article.ImageUrl = found[article.Id];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Các nhà cung cấp AI đã cấu hình, theo thứ tự trong cài đặt (mặc định Cloudflare trước, Gemini sau).</summary>
    private List<IClusterScorer> OrderedScorers(List<string> order)
    {
        var all = new Dictionary<string, IClusterScorer>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloudflare"] = cloudflareChat,
            ["gemini"] = gemini,
        };
        return order.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => all.GetValueOrDefault(name))
            .OfType<IClusterScorer>()
            .Where(s => s.IsConfigured)
            .ToList();
    }

    private static HashSet<string> Tokenize(string text) =>
        NonWordRegex().Split(text.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .ToHashSet();

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial System.Text.RegularExpressions.Regex NonWordRegex();

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count(b.Contains);
        return (double)inter / (a.Count + b.Count - inter);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
