using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyNewsDigest.Web.Options;

namespace MyNewsDigest.Web.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController(IOptions<AuthOptions> options) : ControllerBase
{
    public record LoginRequest(string? Username, string? Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.PasswordHash))
            return Problem("Chưa cấu hình mật khẩu (Auth:PasswordHash).", statusCode: 500);

        if (!string.Equals(req.Username?.Trim(), o.Username, StringComparison.OrdinalIgnoreCase) ||
            !VerifyPassword(o.PasswordHash, req.Password ?? ""))
        {
            await Task.Delay(800); // làm chậm dò mật khẩu
            return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });
        }

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, o.Username) },CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
        return Ok(new { username = o.Username });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { username = User.Identity?.Name });

    private static bool VerifyPassword(string hash, string password)
    {
        try
        {
            return new PasswordHasher<object>().VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
