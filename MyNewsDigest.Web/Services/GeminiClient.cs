using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Models;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Services;

/// <summary>Chấm điểm + tóm tắt bằng Gemini API (dự phòng khi Cloudflare Workers AI lỗi/hết quota).</summary>
public class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> log) : IClusterScorer
{
    public string Name => "Gemini";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public int BatchSize => Math.Max(1, options.Value.BatchSize);

    public async Task<List<ClusterAiResult>> ScoreAsync(IReadOnlyList<ClusterPrompt> clusters, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{o.Model}:generateContent";

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = AiPrompt.System } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = AiPrompt.UserMessage(clusters) } } },
            },
            generationConfig = new
            {
                temperature = 0.3,
                responseMimeType = "application/json",
                // Gemini dùng tập con OpenAPI (kiểu viết hoa)
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        items = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    id = new { type = "INTEGER" },
                                    category = new { type = "STRING", @enum = Categories.Keys },
                                    region = new { type = "STRING", @enum = new[] { "trong-nuoc", "quoc-te" } },
                                    score = new { type = "NUMBER" },
                                    reason = new { type = "STRING" },
                                },
                                required = new[] { "id", "category", "region", "score" },
                            },
                        },
                    },
                    required = new[] { "items" },
                },
            },
        };
        var json = JsonSerializer.Serialize(payload, AiPrompt.PromptJson);

        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", o.ApiKey);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode) return ParseResponse(body);

            if (resp.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError && attempt < 3)
            {
                log.LogWarning("Gemini trả {Status}, thử lại lần {Attempt}", (int)resp.StatusCode, attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(10 * attempt), ct);
                continue;
            }
            throw new HttpRequestException($"Gemini lỗi {(int)resp.StatusCode}: {(body.Length > 400 ? body[..400] : body)}");
        }
    }

    private static List<ClusterAiResult> ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        }
        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Gemini không trả về nội dung.");
        return AiPrompt.ParseResult(text);
    }
}
