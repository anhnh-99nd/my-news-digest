using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyNewsDigest.Web.Data;
using MyNewsDigest.Web.Options;
using MyNewsDigest.Web.Services;

// Tạo hash mật khẩu đăng nhập: dotnet run -- hash-password <mật-khẩu>
if (args is ["hash-password", var plain, ..])
{
    Console.WriteLine(new PasswordHasher<object>().HashPassword(new object(), plain));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<CloudflareOptions>(builder.Configuration.GetSection("Cloudflare"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<RefreshOptions>(builder.Configuration.GetSection("Refresh"));
builder.Services.Configure<TrendsOptions>(builder.Configuration.GetSection("Trends"));

// SQLite: đường dẫn tương đối tính từ thư mục app (IIS không đảm bảo thư mục làm việc)
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/news.db");
if (!Path.IsPathRooted(connection.DataSource))
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection.ToString()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "mnd_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // API trả 401/403 thay vì chuyển hướng; React tự hiện màn đăng nhập
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddHttpClient("feeds", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyNewsDigest/1.0)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Một số báo (Vietnamplus…) luôn trả gzip
    AutomaticDecompression = System.Net.DecompressionMethods.All,
});
builder.Services.AddHttpClient<CloudflareChatClient>(c => c.Timeout = TimeSpan.FromSeconds(180));
builder.Services.AddHttpClient<GeminiClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient<EmbeddingClient>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<FeedFetcher>();
builder.Services.AddScoped<ImageFetcher>();
builder.Services.AddScoped<NewsPipeline>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<DigestService>();
builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services.AddSingleton<ViteAssets>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SourceSeeder.Seed(db, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsync("Lỗi máy chủ");
    }));
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapFallbackToController("Index", "Home");

app.Run();
