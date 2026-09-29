using System.Globalization;
using System.Text;
using MyNewsDigest.Web.Models;

namespace MyNewsDigest.Web.Services;

public static class VnTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "Việt Nam", "Việt Nam");
    }
}

public static class TextHelpers
{
    private const string VietnameseChars = "ăâđêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ";

    public static bool HasVietnamese(string text) =>
        text.Any(ch => VietnameseChars.Contains(char.ToLowerInvariant(ch)));

    /// <summary>Tiêu đề hiển thị của sự kiện: bài sớm nhất có tiêu đề tiếng Việt, tránh tiêu đề tiếng Anh của báo quốc tế.</summary>
    public static string ClusterTitle(Cluster c) =>
        c.Articles.OrderBy(a => a.PublishedAt).FirstOrDefault(a => HasVietnamese(a.Title))?.Title ?? c.Title;

    /// <summary>Ảnh đại diện sự kiện: ảnh của bài sớm nhất có ảnh, ưu tiên báo trong nước.</summary>
    public static string? ClusterImage(Cluster c) =>
        c.Articles
            .Where(a => !string.IsNullOrEmpty(a.ImageUrl))
            .OrderBy(a => a.Source.IsInternational)
            .ThenBy(a => a.PublishedAt)
            .FirstOrDefault()?.ImageUrl is { } url ? UpgradeImage(url) : null;

    /// <summary>RSS của BBC trả ảnh rộng 240px → đổi sang bản 800px cho nét.</summary>
    public static string UpgradeImage(string url) =>
        url.Contains("ichef.bbci.co.uk", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(url, @"/(standard|branded_news|branded_vietnamese)/\d+/", "/$1/800/")
            : url;

    /// <summary>Một nguồn có thể có nhiều link RSS, phân tách bằng dấu phẩy / chấm phẩy / khoảng trắng / xuống dòng.</summary>
    public static string[] FeedUrls(string? feedUrl) =>
        (feedUrl ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Logo báo = favicon của tên miền (qua dịch vụ favicon của Google).</summary>
    public static string? LogoUrl(string? url)
    {
        if (!Uri.TryCreate(FeedUrls(url).FirstOrDefault(), UriKind.Absolute, out var uri)) return null;
        var host = uri.Host;
        foreach (var prefix in new[] { "www.", "feeds.", "rss." })
            if (host.StartsWith(prefix)) host = host[prefix.Length..];
        if (host == "bbci.co.uk") host = "bbc.com";
        return $"https://www.google.com/s2/favicons?domain={host}&sz=64";
    }

    /// <summary>Bỏ dấu + chữ thường, để tìm "bao lu" khớp "Bão lũ".</summary>
    public static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Tạo key dạng slug cho nguồn tin mới.</summary>
    public static string Slug(string text)
    {
        var folded = Fold(text);
        var sb = new StringBuilder();
        foreach (var ch in folded)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "nguon" : slug;
    }
}
