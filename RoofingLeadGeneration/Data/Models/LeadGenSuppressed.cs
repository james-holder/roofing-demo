namespace RoofingLeadGeneration.Data.Models
{
    /// <summary>
    /// Permanently suppressed phone numbers — STOP replies, DNC matches, or manual additions.
    /// Any number in this table must never be texted again.
    /// </summary>
    public class LeadGenSuppressed
    {
        public long     Id           { get; set; }
        public string   Phone        { get; set; } = "";   // E.164 format: +1XXXXXXXXXX
        public string   Reason       { get; set; } = "";   // stop | dnc | manual
        public long?    CampaignId   { get; set; }         // which campaign triggered it, if any
        public DateTime SuppressedAt { get; set; } = DateTime.UtcNow;
    }
}
