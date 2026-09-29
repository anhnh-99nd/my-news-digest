using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Services;

/// <summary>Lấy embedding qua Cloudflare Workers AI (REST) để gom các bài cùng sự kiện.</summary>
public class EmbeddingClient(HttpClient http, IOptions<CloudflareOptions> options, ILogger<EmbeddingClient> log)
{
    private const int BatchSize = 50;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.AccountId) && !string.IsNullOrWhiteSpace(options.Value.ApiToken);

    /// <summary>Trả về mảng cùng độ dài với <paramref name="texts"/>; phần tử null nếu lỗi.</summary>
    public async Task<float[]?[]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var result = new float[]?[texts.Count];
        if (!IsConfigured || texts.Count == 0) return result;

        var o = options.Value;
        var url = $"https://api.cloudflare.com/client/v4/accounts/{o.AccountId}/ai/run/{o.EmbeddingModel}";

        for (var i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { text = batch }) };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    log.LogWarning("Workers AI embedding lỗi {Status}: {Body}", (int)resp.StatusCode, Shorten(body));
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                var j = 0;
                foreach (var vec in doc.RootElement.GetProperty("result").GetProperty("data").EnumerateArray())
                {
                    if (i + j < texts.Count) result[i + j] = vec.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    j++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Không lấy được embedding cho lô {Index}", i);
            }
        }
        return result;
    }

    private static string Shorten(string s) => s.Length > 300 ? s[..300] : s;
}

public static class VectorMath
{
    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var v = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, v, 0, v.Length * sizeof(float));
        return v;
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
