namespace MyNewsDigest.Web.Models;

public class Source
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public bool IsInternational { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastFetchedAt { get; set; }
    public int LastItemCount { get; set; }
    public string? LastError { get; set; }
}

public class Article
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public Source Source { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Ảnh bài báo (từ RSS hoặc og:image). null = chưa thử lấy, "" = đã thử nhưng không có.</summary>
    public string? ImageUrl { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime FetchedAt { get; set; }
    public int? ClusterId { get; set; }
    public Cluster? Cluster { get; set; }
    /// <summary>Vector float32 dạng byte, dùng để gom cụm.</summary>
    public byte[]? Embedding { get; set; }
}

/// <summary>Một sự kiện = nhóm các bài báo cùng nói về một chuyện.</summary>
public class Cluster
{
    public int Id { get; set; }
    /// <summary>Tiêu đề bài gốc đầu tiên của sự kiện (không để AI viết lại).</summary>
    public string Title { get; set; } = "";
    public string Category { get; set; } = Categories.Other;
    public double? AiScore { get; set; }
    /// <summary>AI đánh giá sự kiện xảy ra ở nước ngoài / chủ yếu liên quan nước ngoài. null = chưa chấm.</summary>
    public bool? IsInternational { get; set; }
    public string? AiReason { get; set; }
    public int SourceCount { get; set; }
    public int ArticleCount { get; set; }
    public bool TrendMatch { get; set; }
    public bool NeedsScoring { get; set; } = true;
    public DateTime FirstSeenAt { get; set; }
    /// <summary>Thời điểm đăng của bài mới nhất.</summary>
    public DateTime LastUpdatedAt { get; set; }
    /// <summary>Thời điểm hệ thống thêm bài mới nhất vào cụm (dùng để biết "có cập nhật" sau khi đã đọc).</summary>
    public DateTime LastChangedAt { get; set; }
    public DateTime? ScoredAt { get; set; }

    public DateTime? ReadAt { get; set; }
    /// <summary>Đọc sau: giữ vĩnh viễn, không bị xóa theo thời hạn lưu tin.</summary>
    public bool IsSaved { get; set; }
    public DateTime? SavedAt { get; set; }

    public List<Article> Articles { get; set; } = [];

    public bool IsRead => ReadAt.HasValue && ReadAt.Value >= LastChangedAt;
    public bool HasUpdate => ReadAt.HasValue && LastChangedAt > ReadAt.Value;
}

/// <summary>Cài đặt lưu dạng key → JSON (hiện chỉ dùng key "app").</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Bản tin tổng hợp chốt tại một khung giờ.</summary>
public class Digest
{
    public int Id { get; set; }
    /// <summary>Ngày theo giờ Việt Nam, dạng yyyy-MM-dd.</summary>
    public string LocalDate { get; set; } = "";
    /// <summary>Khung giờ "07:00", hoặc "manual-HHmmss" nếu tạo tay.</summary>
    public string Slot { get; set; } = "";
    public bool IsManual { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DigestItem> Items { get; set; } = [];
}

/// <summary>Một sự kiện trong bản tin; lưu bản chụp để vẫn xem được khi cụm đã bị xóa theo hạn lưu.</summary>
public class DigestItem
{
    public int Id { get; set; }
    public int DigestId { get; set; }
    public Digest Digest { get; set; } = null!;
    public int Rank { get; set; }
    public int? ClusterId { get; set; }
    public Cluster? Cluster { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = Categories.Other;
    public double Score { get; set; }
    public int SourceCount { get; set; }
    public string? Url { get; set; }
}
