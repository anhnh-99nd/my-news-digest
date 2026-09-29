using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyNewsDigest.Web.Services;

namespace MyNewsDigest.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController(SettingsService settings, CloudflareChatClient cloudflare, GeminiClient gemini) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(Wrap(await settings.GetAsync()));

    [HttpPut]
    public async Task<IActionResult> Save(AppSettings input) => Ok(Wrap(await settings.SaveAsync(input)));

    /// <summary>Kèm thông tin phụ cho trang cài đặt: nhà cung cấp AI nào đã có key.</summary>
    private object Wrap(AppSettings s) => new
    {
        settings = s,
        aiConfigured = new { cloudflare = cloudflare.IsConfigured, gemini = gemini.IsConfigured },
    };
}
