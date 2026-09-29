using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyNewsDigest.Web.Services;

namespace MyNewsDigest.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api")]
public class RefreshController(RefreshCoordinator coordinator) : ControllerBase
{
    /// <summary>
    /// Nút "Cập nhật ngay" — cách DUY NHẤT để cập nhật tin (không có job nền, để tiết kiệm quota AI).
    /// Chạy nền, giao diện hỏi tiến trình qua GET api/refresh/status.
    /// </summary>
    [HttpPost("refresh")]
    public IActionResult Start()
    {
        coordinator.TryStart("manual", out _);
        return Accepted(coordinator.Status);
    }

    [HttpGet("refresh/status")]
    public IActionResult Status() => Ok(coordinator.Status);

    [AllowAnonymous]
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("ok");
}
