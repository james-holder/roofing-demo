using Microsoft.EntityFrameworkCore;
using RoofingLeadGeneration.Data;
using RoofingLeadGeneration.Data.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RoofingLeadGeneration.Services
{
    /// <summary>
    /// Orchestrates the Lead Gen SMS pipeline:
    ///   1. Given a campaign, check 30-day re-blast cooldown via contact history
    ///   2. Scrub against internal suppression list + DNC.com registry
    ///   3. Fire Twilio SMS blast (respects quiet hours: 8am–9pm recipient local time)
    ///   4. Record each send attempt in LeadGenContactHistory
    ///   5. Process inbound Twilio webhook replies (STOP handling + lead capture)
    /// TX/OK only. Manual trigger from internal dashboard.
    /// </summary>
    public class LeadGenService
    {
        private readonly AppDbContext             _db;
        private readonly IConfiguration           _config;
        private readonly IHttpClientFactory        _httpFactory;
        private readonly ILogger<LeadGenService>   _logger;

        // Supported states — hard-coded for Phase 1
        public static readonly HashSet<string> SupportedStates =
            new(StringComparer.OrdinalIgnoreCase) { "TX", "OK" };

        // How long to wait before re-blasting the same number (in days)
        private const int ReBlastCooldownDays = 30;

        public LeadGenService(
            AppDbContext           db,
            IConfiguration         config,
            IHttpClientFactory     httpFactory,
            ILogger<LeadGenService> logger)
        {
            _db          = db;
            _config      = config;
            _httpFactory = httpFactory;
            _logger      = logger;
        }

        // ─────────────────────────────────────────────────────────────────
        // Blast: send SMS to parcels in a campaign's storm swath
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fires the SMS blast for a campaign.
        /// Returns (sent, skipped) counts.
        /// </summary>
        public async Task<(int Sent, int Skipped, string Detail)> FireBlastAsync(long campaignId)
        {
            var campaign = await _db.LeadGenCampaigns.FindAsync(campaignId);
            if (campaign == null) throw new InvalidOperationException("Campaign not found.");
            if (campaign.Status != "draft")
                throw new InvalidOperationException($"Campaign is already {campaign.Status}.");
            if (!SupportedStates.Contains(campaign.StateAbbr))
                throw new InvalidOperationException($"State {campaign.StateAbbr} is not supported.");

            var accountSid  = _config["Twilio:AccountSid"]  ?? "";
            var authToken   = _config["Twilio:AuthToken"]   ?? "";
            var fromNumber  = _config["Twilio:FromNumber"]  ?? "";

            if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(fromNumber))
            {
                throw new InvalidOperationException("Twilio credentials are not configured. Add AccountSid, AuthToken, and FromNumber to appsettings.json.");
            }

            campaign.Status = "sending";
            campaign.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Load suppressed numbers for fast lookup
            var suppressed = new HashSet<string>(
                await _db.LeadGenSuppressed.Select(s => s.Phone).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            // 30-day re-blast cooldown: skip numbers we already contacted recently
            var cooldownCutoff = DateTime.UtcNow.AddDays(-ReBlastCooldownDays);
            var recentlyContacted = new HashSet<string>(
                await _db.LeadGenContactHistory
                    .Where(h => h.SentAt >= cooldownCutoff)
                    .Select(h => h.Phone)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            // Also skip numbers already sent in THIS campaign (in case of a resume)
            var sentThisCampaign = new HashSet<string>(
                await _db.LeadGenContactHistory
                    .Where(h => h.CampaignId == campaignId)
                    .Select(h => h.Phone)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            int sent    = 0;
            int skipped = 0;
            var detail  = new System.Text.StringBuilder();

            var targets = await GetCampaignTargetsAsync(campaignId);
            detail.AppendLine($"targets={targets.Count} suppressed={suppressed.Count} cooldown={recentlyContacted.Count} sentThisCampaign={sentThisCampaign.Count}");

            var message = BuildSmsMessage(campaign);

            foreach (var target in targets)
            {
                var phone = NormalizePhone(target.Phone);
                if (string.IsNullOrWhiteSpace(phone)) { detail.AppendLine($"{target.Phone} → SKIP:empty"); skipped++; continue; }
                if (suppressed.Contains(phone))        { detail.AppendLine($"{phone} → SKIP:suppressed"); skipped++; continue; }
                if (sentThisCampaign.Contains(phone))  { detail.AppendLine($"{phone} → SKIP:sentThisCampaign"); skipped++; continue; }
                if (recentlyContacted.Contains(phone)) { detail.AppendLine($"{phone} → SKIP:cooldown"); skipped++; continue; }

                if (!IsWithinQuietHours(DateTime.UtcNow))
                {
                    detail.AppendLine($"{phone} → BREAK:quietHours");
                    _logger.LogWarning("LeadGen blast paused — outside quiet hours (8am–9pm CT).");
                    break;
                }

                var (dncChecked, dncClean) = await CheckDncAsync(phone, campaignId);
                if (dncChecked && !dncClean) { detail.AppendLine($"{phone} → SKIP:dnc"); skipped++; continue; }

                var (ok, smsError) = await SendSmsAsync(accountSid, authToken, fromNumber, phone, message);

                if (ok)
                {
                    var history = new LeadGenContactHistory
                    {
                        Phone      = phone,
                        CampaignId = campaignId,
                        SentAt     = DateTime.UtcNow,
                        DncChecked = dncChecked,
                        DncClean   = dncClean
                    };
                    _db.LeadGenContactHistory.Add(history);
                    await _db.SaveChangesAsync();

                    sent++;
                    sentThisCampaign.Add(phone);
                    recentlyContacted.Add(phone);
                    detail.AppendLine($"{phone} → SENT");
                }
                else
                {
                    detail.AppendLine($"{phone} → SKIP:twilioFailed({smsError})");
                    skipped++;
                }

                await Task.Delay(1100);
            }

            campaign.Status      = "complete";
            campaign.TotalSent  += sent;
            await _db.SaveChangesAsync();

            _logger.LogInformation("LeadGen blast complete: campaignId={Id} sent={Sent} skipped={Skipped}",
                campaignId, sent, skipped);

            return (sent, skipped, detail.ToString());
        }

        // ─────────────────────────────────────────────────────────────────
        // Inbound webhook: process a homeowner reply from Twilio
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the Twilio webhook when a homeowner replies to a blast SMS.
        /// Handles STOP suppression and warm lead capture.
        /// Returns a TwiML response string (empty reply — no auto-response needed).
        /// </summary>
        public async Task<string> ProcessInboundReplyAsync(
            string fromPhone, string body, string? campaignTag = null)
        {
            var phone      = NormalizePhone(fromPhone);
            var bodyTrimmed = (body ?? "").Trim();
            var bodyUpper  = bodyTrimmed.ToUpperInvariant();

            // STOP handling — suppress permanently
            if (bodyUpper is "STOP" or "UNSUBSCRIBE" or "CANCEL" or "END" or "QUIT")
            {
                await SuppressPhoneAsync(phone, "stop", null);
                _logger.LogInformation("LeadGen STOP received from {Phone}", phone);
                // Return empty TwiML — Twilio handles STOP confirmation automatically
                return EmptyTwiml();
            }

            // Find the most recent active campaign for this phone's area
            // (In Phase 1 we use campaignTag embedded in the Twilio webhook URL)
            long? campaignId = null;
            if (long.TryParse(campaignTag, out var parsedId))
                campaignId = parsedId;

            if (campaignId == null)
            {
                // Fall back: find the most recent non-expired campaign
                var latest = await _db.LeadGenCampaigns
                    .Where(c => c.Status == "complete")
                    .OrderByDescending(c => c.SentAt)
                    .FirstOrDefaultAsync();
                campaignId = latest?.Id;
            }

            if (campaignId == null)
            {
                _logger.LogWarning("LeadGen inbound reply from {Phone} — no matching campaign found.", phone);
                return EmptyTwiml();
            }

            var campaign = await _db.LeadGenCampaigns.FindAsync(campaignId.Value);
            if (campaign == null) return EmptyTwiml();

            // Dedup: one lead per phone per campaign
            var existing = await _db.LeadGenLeads
                .FirstOrDefaultAsync(l => l.CampaignId == campaignId && l.HomeownerPhone == phone);

            if (existing != null)
            {
                _logger.LogInformation("LeadGen duplicate reply from {Phone} for campaign {Id} — ignored.", phone, campaignId);
                return EmptyTwiml();
            }

            // Capture warm lead
            var lead = new LeadGenLead
            {
                CampaignId     = campaignId.Value,
                HomeownerPhone = phone,
                HailSizeInches = campaign.HailSizeInches,
                StormDate      = campaign.StormDate,
                ResponseText   = bodyTrimmed,
                RespondedAt    = DateTime.UtcNow,
                Status         = "new"
                // Address/Name populated later when we do reverse skip-trace on the number
            };

            _db.LeadGenLeads.Add(lead);
            campaign.TotalResponded++;
            await _db.SaveChangesAsync();

            // Update the contact history record for this phone + campaign
            var historyRecord = await _db.LeadGenContactHistory
                .FirstOrDefaultAsync(h => h.CampaignId == campaignId && h.Phone == phone);

            if (historyRecord != null)
            {
                historyRecord.Responded    = true;
                historyRecord.ResponseText = bodyTrimmed;
                historyRecord.RespondedAt  = DateTime.UtcNow;
                historyRecord.LeadId       = lead.Id;
                await _db.SaveChangesAsync();
            }
            else
            {
                // No outbound record found (e.g. blast was pre-history or phone mismatch)
                // Log for audit — lead is still captured
                _logger.LogWarning("LeadGen reply from {Phone} campaign {Id} — no contact history row found (lead still captured).",
                    phone, campaignId);
            }

            _logger.LogInformation("LeadGen warm lead captured: phone={Phone} campaign={Id} response=\"{Body}\"",
                phone, campaignId, bodyTrimmed);

            return EmptyTwiml();
        }

        // ─────────────────────────────────────────────────────────────────
        // DNC.com scrub
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scrubs a phone number against the DNC.com National Do-Not-Call Registry.
        /// Returns (checked, clean) where clean=true means safe to contact.
        /// If no API key is configured, returns (false, true) — no check, allow send.
        /// If the number is on the registry, suppresses it and returns (true, false).
        /// </summary>
        private async Task<(bool DncChecked, bool DncClean)> CheckDncAsync(string phone, long campaignId)
        {
            var apiKey = _config["DncCom:ApiKey"] ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // Key not configured — skip DNC check, allow send
                return (false, true);
            }

            try
            {
                using var client = _httpFactory.CreateClient();
                // DNC.com API: GET /v2/scrub?phone={e164}&apiKey={key}
                // Response: { "status": "clean" | "dirty" | "error", ... }
                var url = $"https://api.dnc.com/v2/scrub?phone={Uri.EscapeDataString(phone)}&apiKey={Uri.EscapeDataString(apiKey)}";
                var resp = await client.GetAsync(url);

                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    _logger.LogWarning("DNC.com API error for {Phone}: {Status} {Body}",
                        phone, resp.StatusCode, err.Length > 200 ? err[..200] : err);
                    // On API error, err on the side of caution — skip the number
                    return (true, false);
                }

                var json    = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var status  = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;

                if (string.Equals(status, "clean", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, true);
                }

                // Dirty or unknown → suppress and skip
                _logger.LogInformation("DNC.com flagged {Phone} as '{Status}' — suppressing.", phone, status);
                await SuppressPhoneAsync(phone, "dnc", campaignId);
                return (true, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DNC.com scrub exception for {Phone}", phone);
                // On exception, skip to be safe
                return (true, false);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Suppression
        // ─────────────────────────────────────────────────────────────────

        public async Task SuppressPhoneAsync(string phone, string reason, long? campaignId)
        {
            phone = NormalizePhone(phone);
            if (string.IsNullOrWhiteSpace(phone)) return;

            var existing = await _db.LeadGenSuppressed
                .FirstOrDefaultAsync(s => s.Phone == phone);

            if (existing != null) return; // already suppressed

            _db.LeadGenSuppressed.Add(new LeadGenSuppressed
            {
                Phone        = phone,
                Reason       = reason,
                CampaignId   = campaignId,
                SuppressedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        public async Task<bool> IsPhoneSuppressedAsync(string phone)
        {
            phone = NormalizePhone(phone);
            return await _db.LeadGenSuppressed.AnyAsync(s => s.Phone == phone);
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private async Task<List<(string Phone, string Address)>> GetCampaignTargetsAsync(long campaignId)
        {
            return await _db.LeadGenTargets
                .Where(t => t.CampaignId == campaignId)
                .Select(t => new { t.Phone, t.Address })
                .ToListAsync()
                .ContinueWith(r => r.Result.Select(t => (t.Phone, t.Address)).ToList());
        }

        private static string BuildSmsMessage(LeadGenCampaign campaign)
        {
            var hailDesc = campaign.HailSizeInches >= 1.75 ? "golf ball-sized"
                         : campaign.HailSizeInches >= 1.25 ? "quarter-sized"
                         : campaign.HailSizeInches >= 0.88 ? "penny-sized"
                         : "pea-sized";

            return $"Hail hit your neighborhood on {campaign.StormDate:MMM d} " +
                   $"({hailDesc}, {campaign.HailSizeInches:0.00}\"). " +
                   $"Reply YES for a free roof inspection. Reply STOP to opt out.";
        }

        private async Task<(bool Ok, string Error)> SendSmsAsync(
            string accountSid, string authToken,
            string from, string to, string body)
        {
            try
            {
                using var client = _httpFactory.CreateClient();
                var url     = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
                var payload = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["From"] = from,
                    ["To"]   = to,
                    ["Body"] = body
                });

                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", auth);

                var resp = await client.PostAsync(url, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    _logger.LogWarning("Twilio SMS failed to={To} status={Status} body={Body}",
                        to, resp.StatusCode, err.Length > 200 ? err[..200] : err);
                    return (false, $"HTTP {(int)resp.StatusCode}: {(err.Length > 120 ? err[..120] : err)}");
                }
                return (true, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Twilio SMS exception to={To}", to);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Returns true if the current UTC time is within 8am–9pm Central Time.
        /// TX and OK are both in the Central time zone.
        /// </summary>
        private static bool IsWithinQuietHours(DateTime utcNow)
        {
            try
            {
                // Windows uses the legacy Windows ID; Linux/macOS (Fly.io) requires the IANA ID.
                TimeZoneInfo central;
                try   { central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); }
                catch { central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"); }

                var ct = TimeZoneInfo.ConvertTimeFromUtc(utcNow, central);
                return ct.Hour >= 8 && ct.Hour < 21;
            }
            catch
            {
                // Fallback: allow if we can't determine time zone
                return true;
            }
        }

        /// <summary>Normalize a phone number to E.164 format (+1XXXXXXXXXX).</summary>
        public static string NormalizePhone(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 10) return "+1" + digits;
            if (digits.Length == 11 && digits[0] == '1') return "+" + digits;
            if (raw.StartsWith("+")) return raw; // already E.164
            return digits.Length > 0 ? "+1" + new string(digits.TakeLast(10).ToArray()) : "";
        }

        private static string EmptyTwiml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>";
    }
}
