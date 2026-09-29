namespace MyNewsDigest.Web.Options;

public class AuthOptions
{
    public string Username { get; set; } = "admin";
    public string PasswordHash { get; set; } = "";
}

public class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-flash-latest";
    public int BatchSize { get; set; } = 25;
}

public class CloudflareOptions
{
    public string AccountId { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string EmbeddingModel { get; set; } = "@cf/baai/bge-m3";
    /// <summary>Mô hình chat dùng chấm điểm + tóm tắt (hỗ trợ JSON mode).</summary>
    public string ChatModel { get; set; } = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
    public int ChatBatchSize { get; set; } = 20;
    public int ChatMaxTokens { get; set; } = 2048;
}

public class AiOptions
{
    /// <summary>Thứ tự ưu tiên nhà cung cấp AI: "cloudflare", "gemini". Cái đầu lỗi thì dùng cái sau.</summary>
    /// (Mặc định rỗng vì binder cấu hình nối thêm vào list có sẵn; rỗng = ["cloudflare", "gemini"].)
    public List<string> Providers { get; set; } = [];
    /// <summary>Số cụm tối đa được AI chấm trong một lượt cập nhật.</summary>
    public int MaxClustersPerRun { get; set; } = 100;
}

public class RefreshOptions
{
    /// <summary>Chỉ xét tin trong khoảng này khi gom cụm / chấm điểm.</summary>
    public int WindowHours { get; set; } = 48;
    /// <summary>Ngưỡng cosine để coi 2 bài là cùng sự kiện.</summary>
    public double SimilarityThreshold { get; set; } = 0.8;
    public int RetentionDays { get; set; } = 30;
}

public class TrendsOptions
{
    public bool Enabled { get; set; } = true;
    public string FeedUrl { get; set; } = "https://trends.google.com/trending/rss?geo=VN";
}

public class SourceConfig
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public bool IsInternational { get; set; }
    public bool Enabled { get; set; } = true;
}
