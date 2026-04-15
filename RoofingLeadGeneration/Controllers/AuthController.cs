using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Security.Claims;

namespace RoofingLeadGeneration.Controllers
{
    [Route("[controller]")]
    public class AuthController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AuthController> _logger;

        // Shared DB path (same database as LeadsController)
        internal static readonly string DbPath =
            Path.Combine(AppContext.BaseDirectory, "data", "leads.db");

        private static readonly string ConnStr =
            new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

        public AuthController(IWebHostEnvironment env, ILogger<AuthController> logger)
        {
            _env    = env;
            _logger = logger;
        }

        // ── GET /Auth/Login ─────────────────────────────────────────
        [HttpGet("Login")]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return Redirect(returnUrl ?? "/");

            ViewData["ReturnUrl"]    = returnUrl ?? "/";
            ViewData["GoogleEnabled"]    = !string.IsNullOrWhiteSpace(
                HttpContext.RequestServices.GetService<IConfiguration>()?["Auth:Google:ClientId"]);
            ViewData["MicrosoftEnabled"] = !string.IsNullOrWhiteSpace(
                HttpContext.RequestServices.GetService<IConfiguration>()?["Auth:Microsoft:ClientId"]);

            return View();
        }

        // ── GET /Auth/SignIn/{provider} ─────────────────────────────
        // Kicks off the OAuth flow for "Google" or "Microsoft"
        [HttpGet("SignIn/{provider}")]
        public IActionResult SignIn(string provider, string? returnUrl = "/")
        {
            var props = new AuthenticationProperties
            {
                RedirectUri = Url.Action("Callback", "Auth", new { returnUrl }),
                Items       = { ["provider"] = provider }
            };
            return Challenge(props, provider);
        }

        // ── GET /Auth/Callback ──────────────────────────────────────
        // External provider redirects here after the user approves
        [HttpGet("Callback")]
        public async Task<IActionResult> Callback(string? returnUrl = "/")
        {
            // Read the temporary external cookie set by the OAuth middleware
            var result = await HttpContext.AuthenticateAsync("External");
            if (!result.Succeeded)
            {
                _logger.LogWarning("External auth failed: {Error}", result.Failure?.Message);
                return Redirect("/Auth/Login");
            }

            // Clean up the temporary external cookie
            await HttpContext.SignOutAsync("External");

            var provider   = result.Properties?.Items["provider"] ?? "unknown";
            var providerId = result.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var email      = result.Principal.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var name       = result.Principal.FindFirst(ClaimTypes.Name)?.Value ?? email;

            // Find existing user or create a new one
            long userId = await FindOrCreateUserAsync(provider, providerId, email, name);

            await SignInUserAsync(userId, provider, providerId, email, name);

            return LocalRedirect(returnUrl ?? "/");
        }

        // ── GET /Auth/Logout ────────────────────────────────────────
        [HttpGet("Logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/Auth/Login");
        }

        // ── GET /Auth/DevLogin ──────────────────────────────────────
        // ⚠️  DEV BACKDOOR — only works in Development environment.
        //    Hit this URL to skip OAuth entirely during local debugging.
        //    This route returns 404 in Production.
        [HttpGet("DevLogin")]
        public async Task<IActionResult> DevLogin(string? returnUrl = "/")
        {
            if (!_env.IsDevelopment())
                return NotFound();

            const string devProvider   = "dev";
            const string devProviderId = "dev-user-1";

            long userId = await FindOrCreateUserAsync(
                devProvider, devProviderId,
                "dev@localhost", "Dev User");

            await SignInUserAsync(userId, devProvider, devProviderId,
                "dev@localhost", "Dev User");

            _logger.LogWarning("DEV BACKDOOR used — signed in as Dev User (id={Id})", userId);
            return LocalRedirect(returnUrl ?? "/");
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private async Task SignInUserAsync(
            long userId, string provider, string providerId,
            string email, string name)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, providerId),
                new(ClaimTypes.Name,           name),
                new(ClaimTypes.Email,          email),
                new("provider",                provider),
                new("user_db_id",              userId.ToString())
            };

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(30)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        }

        private async Task<long> FindOrCreateUserAsync(
            string provider, string providerId, string email, string name)
        {
            await EnsureUsersTableAsync();

            using var conn = new SqliteConnection(ConnStr);
            await conn.OpenAsync();

            // Try to find existing user
            using var findCmd = conn.CreateCommand();
            findCmd.CommandText = @"
                SELECT id FROM users
                WHERE provider = $p AND provider_id = $pid";
            findCmd.Parameters.AddWithValue("$p",   provider);
            findCmd.Parameters.AddWithValue("$pid", providerId);

            var existing = await findCmd.ExecuteScalarAsync();
            if (existing is long id) return id;

            // Create new user
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO users (provider, provider_id, email, display_name)
                VALUES ($p, $pid, $email, $name);
                SELECT last_insert_rowid();";
            insertCmd.Parameters.AddWithValue("$p",     provider);
            insertCmd.Parameters.AddWithValue("$pid",   providerId);
            insertCmd.Parameters.AddWithValue("$email", email);
            insertCmd.Parameters.AddWithValue("$name",  name);

            var newId = await insertCmd.ExecuteScalarAsync();
            return Convert.ToInt64(newId);
        }

        private static bool _usersTableReady = false;
        private static readonly SemaphoreSlim _usersLock = new(1, 1);

        private async Task EnsureUsersTableAsync()
        {
            if (_usersTableReady) return;
            await _usersLock.WaitAsync();
            try
            {
                if (_usersTableReady) return;

                Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
                using var conn = new SqliteConnection(ConnStr);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id           INTEGER PRIMARY KEY AUTOINCREMENT,
                        provider     TEXT NOT NULL,
                        provider_id  TEXT NOT NULL,
                        email        TEXT,
                        display_name TEXT,
                        created_at   TEXT NOT NULL DEFAULT (datetime('now')),
                        UNIQUE(provider, provider_id)
                    );";
                await cmd.ExecuteNonQueryAsync();
                _usersTableReady = true;
            }
            finally { _usersLock.Release(); }
        }
    }
}
