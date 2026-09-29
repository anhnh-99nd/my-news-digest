using System.Text.Encodings.Web;
using System.Text.Json;
using MyNewsDigest.Web.Models;

namespace MyNewsDigest.Web.Services;

public record ClusterPrompt(int Id, List<string> Sources, List<string> Titles, List<string> Snippets);

public class ClusterAiResult
{
    public int Id { get; set; }
    public string? Category { get; set; }
    public string? Region { get; set; }
    public double Score { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Một nhà cung cấp AI chấm điểm + tóm tắt cụm tin (Cloudflare Workers AI, Gemini…).</summary>
public interface IClusterScorer
{
    string Name { get; }
    bool IsConfigured { get; }
    /// <summary>Số cụm tối đa gửi trong một lần gọi.</summary>
    int BatchSize { get; }
    Task<List<ClusterAiResult>> ScoreAsync(IReadOnlyList<ClusterPrompt> clusters, CancellationToken ct);
}

/// <summary>Prompt và JSON schema dùng chung cho mọi nhà cung cấp AI.</summary>
public static class AiPrompt
{
    public const string System = """
        Bạn là biên tập viên tin tức Việt Nam. Mỗi mục đầu vào là một CỤM TIN: các bài báo từ nhiều báo khác nhau cùng nói về một sự kiện.
        Với mỗi cụm, trả về:
        - id: giữ nguyên id đầu vào.
        - category: một trong các mã: thoi-su, the-gioi, kinh-te, cong-nghe, the-thao, giai-tri, phap-luat, doi-song, giao-duc, suc-khoe, khac.
        - region: "trong-nuoc" nếu sự kiện xảy ra ở Việt Nam hoặc chủ yếu liên quan người/tổ chức Việt Nam (kể cả VĐV, nghệ sĩ Việt thi đấu/biểu diễn ở nước ngoài); "quoc-te" nếu sự kiện ở nước ngoài và không chủ yếu liên quan Việt Nam (kể cả tin giải trí, thể thao, công nghệ nước ngoài).
        - score: độ hot/nổi bật từ 1 đến 10 (số thập phân được). Căn cứ: tầm ảnh hưởng tới nhiều người, tính bất ngờ/giật gân, mức độ công chúng quan tâm, số báo cùng đưa tin (nhiều báo = hot hơn). Tin PR, quảng cáo, mẹo vặt, tử vi: 1-2. Tin thường ngày: 3-5. Tin đáng chú ý: 6-7. Tin rất nóng, được bàn tán rộng: 8-10.
        - reason: lý do chấm điểm, tối đa 15 từ.
        Chuyên mục: tin trong nước (thiên tai, giao thông, chính sách Việt Nam…) KHÔNG phải the-gioi; the-gioi chỉ dành cho sự kiện ở nước ngoài.
        Trả lời đúng một đối tượng JSON dạng {"items": [...]}, mỗi cụm đầu vào có đúng một phần tử; reason viết bằng tiếng Việt.
        """;

    public static readonly JsonSerializerOptions PromptJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static readonly JsonSerializerOptions ResultJson = new() { PropertyNameCaseInsensitive = true };

    public static string UserMessage(IReadOnlyList<ClusterPrompt> clusters) => JsonSerializer.Serialize(clusters, PromptJson);

    /// <summary>JSON Schema chuẩn (Cloudflare) cho {"items": [...]}.</summary>
    public static object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            items = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "integer" },
                        category = new { type = "string", @enum = Categories.Keys },
                        region = new { type = "string", @enum = new[] { "trong-nuoc", "quoc-te" } },
                        score = new { type = "number" },
                        reason = new { type = "string" },
                    },
                    required = new[] { "id", "category", "region", "score" },
                },
            },
        },
        required = new[] { "items" },
    };

    private sealed class Wrapper
    {
        public List<ClusterAiResult>? Items { get; set; }
    }

    /// <summary>Đọc kết quả dạng {"items":[...]} hoặc mảng trần; bỏ rào ```json nếu model tự thêm.</summary>
    public static List<ClusterAiResult> ParseResult(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine > 0 && lastFence > firstLine) text = text[(firstLine + 1)..lastFence].Trim();
        }
        if (text.StartsWith('['))
            return JsonSerializer.Deserialize<List<ClusterAiResult>>(text, ResultJson) ?? [];
        return JsonSerializer.Deserialize<Wrapper>(text, ResultJson)?.Items ?? [];
    }
}
