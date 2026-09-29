using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Services;

/// <summary>Chấm điểm + tóm tắt bằng mô hình chat của Cloudflare Workers AI (JSON mode).</summary>
public class CloudflareChatClient(HttpClient http, IOptions<CloudflareOptions> options, ILogger<CloudflareChatClient> log)
    : IClusterScorer
{
    public string Name => "Cloudflare Workers AI";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.AccountId) && !string.IsNullOrWhiteSpace(options.Value.ApiToken) &&
        !string.IsNullOrWhiteSpace(options.Value.ChatModel);

    public int BatchSize => Math.Max(1, options.Value.ChatBatchSize);

    public async Task<List<ClusterAiResult>> ScoreAsync(IReadOnlyList<ClusterPrompt> clusters, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"https://api.cloudflare.com/client/v4/accounts/{o.AccountId}/ai/run/{o.ChatModel}";
        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = AiPrompt.System },
                new { role = "user", content = AiPrompt.UserMessage(clusters) },
            },
            response_format = new { type = "json_schema", json_schema = AiPrompt.JsonSchema },
            max_tokens = o.ChatMaxTokens,
            temperature = 0.3,
        };
        var json = JsonSerializer.Serialize(payload, AiPrompt.PromptJson);

        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode) return ParseResponse(body);

            if (resp.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError && attempt < 3)
            {
                log.LogWarning("Workers AI trả {Status}, thử lại lần {Attempt}", (int)resp.StatusCode, attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }
            throw new HttpRequestException($"Workers AI lỗi {(int)resp.StatusCode}: {(body.Length > 400 ? body[..400] : body)}");
        }
    }

    private static List<ClusterAiResult> ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("response", out var response))
            throw new InvalidOperationException("Workers AI không trả về nội dung.");

        // JSON mode: "response" có thể đã là object, hoặc là chuỗi JSON
        return response.ValueKind == JsonValueKind.String
            ? AiPrompt.ParseResult(response.GetString() ?? "")
            : AiPrompt.ParseResult(response.GetRawText());
    }
}
