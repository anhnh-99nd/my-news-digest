using System.Text;
using System.Text.RegularExpressions;

namespace MyNewsDigest.Web.Services;

/// <summary>Lấy ảnh đại diện (og:image / twitter:image) từ trang bài báo khi RSS không có ảnh.</summary>
public partial class ImageFetcher(IHttpClientFactory httpFactory, ILogger<ImageFetcher> log)
{
    /// <summary>Chỉ đọc phần đầu trang (thẻ meta nằm trong head).</summary>
    private const int MaxBytes = 256 * 1024;

    public async Task<string?> FetchOgImageAsync(string articleUrl, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("feeds");
            using var resp = await http.GetAsync(articleUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[MaxBytes];
            var read = 0;
            int n;
            while (read < MaxBytes && (n = await stream.ReadAsync(buffer.AsMemory(read, MaxBytes - read), ct)) > 0) read += n;
            var html = Encoding.UTF8.GetString(buffer, 0, read);

            var m = OgImageRegex().Match(html);
            if (!m.Success) m = OgImageReversedRegex().Match(html);
            return m.Success ? FeedFetcher.ToAbsolute(m.Groups[1].Value, resp.RequestMessage?.RequestUri ?? new Uri(articleUrl)) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogDebug(ex, "Không lấy được og:image của {Url}", articleUrl);
            return null;
        }
    }

    [GeneratedRegex("""<meta[^>]+(?:property|name)\s*=\s*["'](?:og:image|twitter:image)(?::src)?["'][^>]*content\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgImageRegex();

    [GeneratedRegex("""<meta[^>]+content\s*=\s*["']([^"']+)["'][^>]*(?:property|name)\s*=\s*["'](?:og:image|twitter:image)(?::src)?["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgImageReversedRegex();
}
