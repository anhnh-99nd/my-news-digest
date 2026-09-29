namespace MyNewsDigest.Web.Models;

public static class Categories
{
    public const string Other = "khac";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["thoi-su"] = "Thời sự",
        ["the-gioi"] = "Thế giới",
        ["kinh-te"] = "Kinh tế",
        ["cong-nghe"] = "Công nghệ",
        ["the-thao"] = "Thể thao",
        ["giai-tri"] = "Giải trí",
        ["phap-luat"] = "Pháp luật",
        ["doi-song"] = "Đời sống",
        ["giao-duc"] = "Giáo dục",
        ["suc-khoe"] = "Sức khỏe",
        [Other] = "Khác",
    };

    public static string[] Keys => [.. Labels.Keys];

    public static string Normalize(string? key) =>
        key is not null && Labels.ContainsKey(key.Trim().ToLowerInvariant()) ? key.Trim().ToLowerInvariant() : Other;

    public static string Label(string key) => Labels.TryGetValue(key, out var l) ? l : Labels[Other];
}
