using Microsoft.AspNetCore.Authentication.Cookies;
using RoofingLeadGeneration.Services;

var builder = WebApplication.CreateBuilder(args);
var config  = builder.Configuration;

// ── MVC ───────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ── Authentication ────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)

    // Main session cookie (30-day sliding window)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opt =>
    {
        opt.LoginPath        = "/Auth/Login";
        opt.LogoutPath       = "/Auth/Logout";
        opt.AccessDeniedPath = "/Auth/Login";
        opt.ExpireTimeSpan   = TimeSpan.FromDays(30);
        opt.SlidingExpiration = true;
        opt.Cookie.Name      = ".StormLead.Session";
        opt.Cookie.HttpOnly  = true;
        opt.Cookie.SameSite  = SameSiteMode.Lax;
    })

    // Temporary cookie used while the OAuth round-trip is in flight
    .AddCookie("External", opt =>
    {
        opt.Cookie.Name    = ".StormLead.External";
        opt.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    })

    // Google OAuth  –  register at https://console.cloud.google.com
    //   Authorised redirect URI to add: https://yourdomain/signin-google
    ;

if (!string.IsNullOrWhiteSpace(config["Auth:Google:ClientId"]))
{
    builder.Services.AddAuthentication()
        .AddGoogle("Google", opt =>
        {
            opt.SignInScheme = "External";
            opt.ClientId     = config["Auth:Google:ClientId"]!;
            opt.ClientSecret = config["Auth:Google:ClientSecret"]!;
        });
}

if (!string.IsNullOrWhiteSpace(config["Auth:Microsoft:ClientId"]))
{
    builder.Services.AddAuthentication()
        .AddMicrosoftAccount("Microsoft", opt =>
        {
            opt.SignInScheme = "External";
            opt.ClientId     = config["Auth:Microsoft:ClientId"]!;
            opt.ClientSecret = config["Auth:Microsoft:ClientSecret"]!;
        });
}

// ── HttpClient factory (RealDataService) ─────────────────────────────────
builder.Services.AddHttpClient("overpass", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent", "StormLeadPro/1.0");
});
builder.Services.AddHttpClient("noaa", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent", "StormLeadPro/1.0");
});
builder.Services.AddHttpClient("regrid", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RealDataService>();

// ── Pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // ← must come before UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
