using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoofingLeadGeneration.Data;
using RoofingLeadGeneration.Data.Models;
using RoofingLeadGeneration.Services;
using System.Security.Claims;

namespace RoofingLeadGeneration.Controllers
{
    /// <summary>
    /// Internal-only lead gen dashboard and Twilio webhook.
    /// Restricted to the configured admin email address.
    /// Routes: /LeadGen/*
    /// </summary>
    [Authorize]
    [Route("LeadGen")]
    public class LeadGenController : Controller
    {
        private readonly AppDbContext    _db;
        private readonly LeadGenService  _leadGen;
        private readonly IConfiguration  _config;
        private readonly string          _adminEmail;

        public LeadGenController(
            AppDbContext   db,
            LeadGenService leadGen,
            IConfiguration config)
        {
            _db         = db;
            _leadGen    = leadGen;
            _config     = config;
            _adminEmail = config["AdminEmail"] ?? "";
        }

        private bool IsAdmin() =>
            (User.FindFirst(ClaimTypes.Email)?.Value ?? "") == _adminEmail;

        // ── GET /LeadGen ─────────────────────────────────────────────────
        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return Redirect("/");

            var campaigns = await _db.LeadGenCampaigns
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var leads = await _db.LeadGenLeads
                .Include(l => l.Campaign)
                .OrderByDescending(l => l.RespondedAt)
                .Take(200)
                .ToListAsync();

            var suppressed = await _db.LeadGenSuppressed.CountAsync();

            ViewBag.Campaigns       = campaigns;
            ViewBag.Leads           = leads;
            ViewBag.Suppressed      = suppressed;
            ViewBag.GoogleMapsApiKey = _config["GoogleMaps:ApiKey"] ?? "";
            return View();
        }

        // ── POST /LeadGen/CreateCampaign ─────────────────────────────────
        [HttpPost("CreateCampaign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCampaign(
            string stateAbbr, DateTime stormDate, double hailSizeInches,
            double centerLat, double centerLng, double radiusMiles, string? notes)
        {
            if (!IsAdmin()) return Redirect("/");

            if (!LeadGenService.SupportedStates.Contains(stateAbbr))
                return BadRequest(new { error = "Only TX and OK are supported." });

            var campaign = new LeadGenCampaign
            {
                StateAbbr      = stateAbbr.ToUpper(),
                StormDate      = stormDate,
                HailSizeInches = hailSizeInches,
                CenterLat      = centerLat,
                CenterLng      = centerLng,
                RadiusMiles    = radiusMiles,
                Notes          = notes ?? "",
                Status         = "draft",
                CreatedAt      = DateTime.UtcNow
            };

            _db.LeadGenCampaigns.Add(campaign);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Campaign #{campaign.Id} created.";
            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/UpdateCampaign ────────────────────────────────
        [HttpPost("UpdateCampaign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCampaign(
            long campaignId, string stateAbbr, DateTime stormDate, double hailSizeInches,
            double centerLat, double centerLng, double radiusMiles, string? notes)
        {
            if (!IsAdmin()) return Redirect("/");

            var campaign = await _db.LeadGenCampaigns.FindAsync(campaignId);
            if (campaign == null) { TempData["Error"] = "Campaign not found."; return RedirectToAction("Index"); }

            // For sent/complete campaigns only notes is editable
            if (campaign.Status == "draft")
            {
                campaign.StateAbbr      = stateAbbr.ToUpper();
                campaign.StormDate      = stormDate;
                campaign.HailSizeInches = hailSizeInches;
                campaign.CenterLat      = centerLat;
                campaign.CenterLng      = centerLng;
                campaign.RadiusMiles    = radiusMiles;
            }
            campaign.Notes = notes ?? "";

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Campaign #{campaign.Id} updated.";
            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/FireBlast ──────────────────────────────────────
        [HttpPost("FireBlast")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FireBlast(long campaignId)
        {
            if (!IsAdmin()) return Redirect("/");

            try
            {
                var (sent, skipped) = await _leadGen.FireBlastAsync(campaignId);
                TempData["Success"] = $"Blast complete — {sent} sent, {skipped} skipped.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/UpdateLeadStatus ───────────────────────────────
        [HttpPost("UpdateLeadStatus")]
        public async Task<IActionResult> UpdateLeadStatus(long leadId, string status)
        {
            if (!IsAdmin()) return Unauthorized();

            var lead = await _db.LeadGenLeads.FindAsync(leadId);
            if (lead == null) return NotFound();

            var allowed = new[] { "new", "available", "sold", "expired" };
            if (!allowed.Contains(status)) return BadRequest();

            lead.Status = status;
            await _db.SaveChangesAsync();
            return Ok(new { status });
        }

        // ── POST /LeadGen/Suppress ───────────────────────────────────────
        [HttpPost("Suppress")]
        public async Task<IActionResult> Suppress(string phone)
        {
            if (!IsAdmin()) return Unauthorized();
            await _leadGen.SuppressPhoneAsync(phone, "manual", null);
            return Ok(new { suppressed = true });
        }

        // ── POST /LeadGen/Webhook ────────────────────────────────────────
        // Twilio calls this URL when a homeowner replies to a blast SMS.
        // Configure in Twilio Console → Phone Numbers → Messaging → Webhook URL.
        // Optionally append ?campaign={id} to route replies to the right campaign.
        // This endpoint must NOT require authentication (Twilio can't log in).
        [AllowAnonymous]
        [HttpPost("Webhook")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook(
            [FromForm] string? From,
            [FromForm] string? Body,
            [FromQuery] string? campaign)
        {
            if (string.IsNullOrWhiteSpace(From)) return Ok();

            var twiml = await _leadGen.ProcessInboundReplyAsync(From, Body ?? "", campaign);
            return Content(twiml, "application/xml");
        }
    }
}
