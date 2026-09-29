namespace MyNewsDigest.Web.Services;

public record RefreshStatus
{
    public bool IsRunning { get; init; }
    public string Stage { get; init; } = "idle";
    public string? Message { get; init; }
    public string? Trigger { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? LastError { get; init; }
    public RefreshResult? LastResult { get; init; }
}

/// <summary>Đảm bảo mỗi lúc chỉ có một lượt cập nhật chạy, và lưu tiến trình để giao diện hiển thị.</summary>
public class RefreshCoordinator(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<RefreshCoordinator> log)
{
    private readonly Lock _gate = new();
    private Task _current = Task.CompletedTask;
    private RefreshStatus _status = new();

    public RefreshStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>Bắt đầu cập nhật nếu chưa có lượt nào đang chạy. Luôn trả về task của lượt hiện tại.</summary>
    public bool TryStart(string trigger, out Task task)
    {
        lock (_gate)
        {
            if (!_current.IsCompleted)
            {
                task = _current;
                return false;
            }
            _status = _status with
            {
                IsRunning = true,
                Stage = "start",
                Message = "Bắt đầu cập nhật…",
                Trigger = trigger,
                StartedAt = DateTime.UtcNow,
                FinishedAt = null,
                LastError = null,
            };
            _current = task = Task.Run(() => RunAsync(lifetime.ApplicationStopping));
            return true;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<NewsPipeline>();
            var result = await pipeline.RunAsync((stage, message) => Update(s => s with { Stage = stage, Message = message }), ct);

            // Không có job nền: bản tin theo giờ được chốt ở lượt cập nhật đầu tiên sau khung giờ
            var digest = await scope.ServiceProvider.GetRequiredService<DigestService>().CreateScheduledIfDueAsync(ct);
            var summary = digest is null ? result.Summary : $"{result.Summary}. Đã chốt bản tin {digest.Slot}";

            Update(s => s with
            {
                IsRunning = false,
                Stage = "done",
                Message = summary,
                FinishedAt = DateTime.UtcNow,
                LastResult = result,
            });
            log.LogInformation("{Summary}", result.Summary);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cập nhật tin thất bại");
            Update(s => s with
            {
                IsRunning = false,
                Stage = "error",
                Message = "Cập nhật thất bại: " + ex.Message,
                FinishedAt = DateTime.UtcNow,
                LastError = ex.Message,
            });
        }
    }

    private void Update(Func<RefreshStatus, RefreshStatus> change)
    {
        lock (_gate) _status = change(_status);
    }
}
