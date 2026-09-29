using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MyNewsDigest.Web.Services;

public record FeedItem(string Title, string Url, string? Description, DateTime? PublishedAt, string? ImageUrl = null);

/// <summary>Tải và đọc RSS 2.0 / Atom.</summary>
public partial class FeedFetcher(IHttpClientFactory httpFactory)
{
    public async Task<List<FeedItem>> FetchAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("feeds");
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, CheckCharacters = false, Async = true };
        using var reader = XmlReader.Create(stream, settings);
        var doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        return Parse(doc);
    }

    public static List<FeedItem> Parse(XDocument doc)
    {
        var items = new List<FeedItem>();
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            var title = CleanText(Child(el, "title"));
            var link = Child(el, "link");
            if (string.IsNullOrWhiteSpace(link))
                link = el.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && e.Attribute("href") != null)
                    ?.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(link)) link = Child(el, "guid");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;
            if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)) continue;

            var rawDesc = Child(el, "description") ?? Child(el, "summary") ?? Child(el, "encoded");
            var desc = CleanText(rawDesc);
            var date = ParseDate(Child(el, "pubDate") ?? Child(el, "published") ?? Child(el, "updated") ?? Child(el, "date"));
            var image = FindImage(el, rawDesc, uri);
            items.Add(new FeedItem(title, NormalizeUrl(uri), Truncate(desc, 500), date, image));
        }
        return items;
    }

    /// <summary>Ảnh của bài: enclosure / media:content / media:thumbnail, rồi thẻ img đầu tiên trong mô tả.</summary>
    private static string? FindImage(XElement el, string? rawDescription, Uri articleUri)
    {
        var candidates = el.Elements()
            .Where(e => e.Name.LocalName is "enclosure" or "content" or "thumbnail")
            .Where(e => e.Name.LocalName != "enclosure" || (e.Attribute("type")?.Value ?? "image").StartsWith("image"))
            .Select(e => e.Attribute("url")?.Value)
            .ToList();
        // media:group chứa nhiều media:content
        candidates.AddRange(el.Elements().Where(e => e.Name.LocalName == "group")
            .SelectMany(g => g.Elements()).Select(e => e.Attribute("url")?.Value));
        if (!string.IsNullOrEmpty(rawDescription))
        {
            var m = ImgRegex().Match(WebUtility.HtmlDecode(rawDescription));
            if (m.Success) candidates.Add(m.Groups[1].Value);
        }
        return candidates.Select(c => ToAbsolute(c, articleUri)).FirstOrDefault(c => c != null);
    }

    public static string? ToAbsolute(string? url, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = WebUtility.HtmlDecode(url.Trim());
        if (url.StartsWith("//")) url = "https:" + url;
        return Uri.TryCreate(baseUri, url, out var abs) && abs.Scheme is "http" or "https" ? abs.AbsoluteUri : null;
    }

    private static string? Child(XElement el, string localName) =>
        el.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    public static string CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = TagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        return SpaceRegex().Replace(text, " ").Trim();
    }

    private static string? Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max].TrimEnd() + "…";

    /// <summary>Bỏ tham số utm_* và fragment để cùng một bài không bị lưu trùng.</summary>
    public static string NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = "" };
        if (!string.IsNullOrEmpty(builder.Query))
        {
            var kept = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase));
            builder.Query = string.Join('&', kept);
        }
        return builder.Uri.AbsoluteUri;
    }

    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        s = Regex.Replace(s, @"\s(GMT|UT|UTC)$", " +00:00");
        s = OffsetRegex().Replace(s, "$1:$2"); // +0700 → +07:00
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;
        // Bỏ tên thứ ("Sun, ") nếu không khớp ngày
        var comma = s.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 0 && comma < 5 &&
            DateTimeOffset.TryParse(s[(comma + 2)..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
            return dto.UtcDateTime;
        return null;
    }

    [GeneratedRegex("""<img[^>]+?(?:data-src|src)s*=s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetRegex();
}
