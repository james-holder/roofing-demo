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

            var targets = await _db.LeadGenTargets
                .OrderBy(t => t.CampaignId).ThenBy(t => t.AddedAt)
                .ToListAsync();

            ViewBag.Campaigns        = campaigns;
            ViewBag.Leads            = leads;
            ViewBag.Suppressed       = suppressed;
            ViewBag.Targets          = targets;
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

        // ── GET /LeadGen/Debug/{campaignId} ─────────────────────────────
        [HttpGet("Debug/{campaignId}")]
        public async Task<IActionResult> Debug(long campaignId)
        {
            if (!IsAdmin()) return Redirect("/");

            var targets   = await _db.LeadGenTargets.Where(t => t.CampaignId == campaignId).ToListAsync();
            var suppressed = await _db.LeadGenSuppressed.Select(s => s.Phone).ToListAsync();
            var cooldown  = await _db.LeadGenContactHistory
                .Where(h => h.SentAt >= DateTime.UtcNow.AddDays(-30))
                .Select(h => new { h.Phone, h.CampaignId, h.SentAt })
                .ToListAsync();
            var sentThis  = await _db.LeadGenContactHistory
                .Where(h => h.CampaignId == campaignId)
                .Select(h => h.Phone)
                .ToListAsync();

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Campaign {campaignId} — {targets.Count} target(s)\n");
            foreach (var t in targets)
            {
                var phone = LeadGenService.NormalizePhone(t.Phone);
                var reasons = new List<string>();
                if (string.IsNullOrWhiteSpace(phone))       reasons.Add("EMPTY after normalize");
                if (suppressed.Contains(phone))             reasons.Add("SUPPRESSED");
                if (sentThis.Contains(phone))               reasons.Add("ALREADY SENT THIS CAMPAIGN");
                var cool = cooldown.FirstOrDefault(h => h.Phone == phone);
                if (cool != null) reasons.Add($"COOLDOWN (sent {cool.SentAt:MMM d HH:mm} campaign #{cool.CampaignId})");
                var status = reasons.Any() ? "SKIP: " + string.Join(", ", reasons) : "OK — would send";
                lines.AppendLine($"Raw: {t.Phone}  →  Normalized: {phone}  →  {status}");
            }
            lines.AppendLine($"\nAll cooldown phones: {string.Join(", ", cooldown.Select(h => h.Phone))}");
            lines.AppendLine($"All suppressed phones: {string.Join(", ", suppressed)}");

            return Content(lines.ToString(), "text/plain");
        }

        // ── POST /LeadGen/FireBlast ──────────────────────────────────────
        [HttpPost("FireBlast")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FireBlast(long campaignId)
        {
            if (!IsAdmin()) return Redirect("/");

            try
            {
                var (sent, skipped, detail) = await _leadGen.FireBlastAsync(campaignId);
                TempData["Success"] = $"Blast complete — {sent} sent, {skipped} skipped. | {detail.Replace("\n", " | ")}";
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

        // ── POST /LeadGen/ResetCampaign ─────────────────────────────────
        [HttpPost("ResetCampaign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetCampaign(long campaignId)
        {
            if (!IsAdmin()) return Redirect("/");

            var campaign = await _db.LeadGenCampaigns.FindAsync(campaignId);
            if (campaign == null) { TempData["Error"] = "Campaign not found."; return RedirectToAction("Index"); }

            // Get all phones targeted by this campaign
            var targetPhones = await _db.LeadGenTargets
                .Where(t => t.CampaignId == campaignId)
                .Select(t => t.Phone)
                .ToListAsync();

            // Clear contact history for this campaign AND the 30-day global cooldown
            // for these specific phones so they can be re-blasted immediately
            var history = await _db.LeadGenContactHistory
                .Where(h => h.CampaignId == campaignId || targetPhones.Contains(h.Phone))
                .ToListAsync();
            _db.LeadGenContactHistory.RemoveRange(history);

            campaign.Status    = "draft";
            campaign.TotalSent = 0;
            campaign.SentAt    = null;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Campaign #{campaignId} reset to draft.";
            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/ClearContactHistory ───────────────────────────
        [HttpPost("ClearContactHistory")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearContactHistory()
        {
            if (!IsAdmin()) return Redirect("/");
            var all = await _db.LeadGenContactHistory.ToListAsync();
            _db.LeadGenContactHistory.RemoveRange(all);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Cleared {all.Count} contact history records.";
            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/AddTarget ──────────────────────────────────────
        [HttpPost("AddTarget")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTarget(long campaignId, string phone, string? address)
        {
            if (!IsAdmin()) return Redirect("/");

            var campaign = await _db.LeadGenCampaigns.FindAsync(campaignId);
            if (campaign == null) { TempData["Error"] = "Campaign not found."; return RedirectToAction("Index"); }
            if (campaign.Status != "draft") { TempData["Error"] = "Can only add targets to draft campaigns."; return RedirectToAction("Index"); }

            var normalized = LeadGenService.NormalizePhone(phone);
            if (string.IsNullOrWhiteSpace(normalized)) { TempData["Error"] = "Invalid phone number."; return RedirectToAction("Index"); }

            var exists = await _db.LeadGenTargets
                .AnyAsync(t => t.CampaignId == campaignId && t.Phone == normalized);
            if (!exists)
            {
                _db.LeadGenTargets.Add(new Data.Models.LeadGenTarget
                {
                    CampaignId = campaignId,
                    Phone      = normalized,
                    Address    = address ?? "",
                    AddedAt    = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = $"Target {normalized} added to campaign #{campaignId}.";
            return RedirectToAction("Index");
        }

        // ── POST /LeadGen/RemoveTarget ───────────────────────────────────
        [HttpPost("RemoveTarget")]
        public async Task<IActionResult> RemoveTarget(long targetId)
        {
            if (!IsAdmin()) return Unauthorized();

            var target = await _db.LeadGenTargets.FindAsync(targetId);
            if (target == null) return NotFound();

            _db.LeadGenTargets.Remove(target);
            await _db.SaveChangesAsync();
            return Ok(new { removed = true });
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
