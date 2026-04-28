namespace RoofingLeadGeneration.Data.Models
{
    /// <summary>
    /// One row per phone number per campaign blast attempt.
    /// Created when we SEND a message (not just when they respond).
    /// This gives us:
    ///   - Full outreach history per phone number across all campaigns
    ///   - "Don't re-blast" logic: skip numbers contacted in last N days
    ///   - Audit trail for TCPA compliance (when we sent, DNC status at send time)
    /// </summary>
    public class LeadGenContactHistory
    {
        public long      Id           { get; set; }
        public string    Phone        { get; set; } = "";   // E.164 format
        public long      CampaignId   { get; set; }
        public DateTime  SentAt       { get; set; } = DateTime.UtcNow;

        // DNC check result at time of send
        public bool      DncChecked   { get; set; } = false;
        public bool      DncClean     { get; set; } = false;  // true = passed scrub

        // Response (populated by inbound webhook if they reply)
        public bool      Responded    { get; set; } = false;
        public string?   ResponseText { get; set; }
        public DateTime? RespondedAt  { get; set; }

        // FK to LeadGenLead if a warm lead was created from this response
        public long?     LeadId       { get; set; }

        // Nav
        public LeadGenCampaign?  Campaign { get; set; }
        public LeadGenLead?      Lead     { get; set; }
    }
}
