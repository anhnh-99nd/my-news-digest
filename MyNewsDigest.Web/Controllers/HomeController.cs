using Microsoft.AspNetCore.Mvc;

namespace MyNewsDigest.Web.Controllers;

/// <summary>Trả trang khung (_Layout chỉ có div#root); mọi giao diện do React đảm nhiệm.</summary>
public class HomeController : Controller
{
    public IActionResult Index() => View();
}
