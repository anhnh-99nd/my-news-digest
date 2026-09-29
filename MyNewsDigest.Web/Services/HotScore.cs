using MyNewsDigest.Web.Models;

namespace MyNewsDigest.Web.Services;

public static class HotScore
{
    /// <summary>Thời gian bán rã khi xếp hạng: tin cũ 18 giờ bị giảm một nửa thứ hạng.</summary>
    private const double HalfLifeHours = 18;

    /// <summary>
    /// Điểm hiển thị 1–10: điểm AI + thưởng khi nhiều báo cùng đưa tin + thưởng khi khớp Google Trends.
    /// Cụm chưa được AI chấm thì ước lượng theo số báo.
    /// </summary>
    public static double Display(Cluster c)
    {
        var ai = c.AiScore ?? Math.Min(2 + c.SourceCount, 7);
        var coverage = Math.Min(Math.Max(c.SourceCount - 1, 0), 6) * 0.25;
        var trend = c.TrendMatch ? 0.5 : 0;
        return Math.Round(Math.Clamp(ai + coverage + trend, 1, 10), 1);
    }

    /// <summary>Thứ hạng = điểm hiển thị giảm dần theo tuổi của tin.</summary>
    public static double Rank(Cluster c, DateTime nowUtc)
    {
        var ageHours = Math.Max(0, (nowUtc - c.LastUpdatedAt).TotalHours);
        return Display(c) * Math.Pow(0.5, ageHours / HalfLifeHours);
    }
}
