namespace RoofingLeadGeneration.Data.Models
{
    /// <summary>
    /// A warm lead captured when a homeowner replies to a campaign SMS blast.
    /// Any reply other than STOP/UNSUBSCRIBE creates a lead record.
    /// </summary>
    public class LeadGenLead
    {
        public long     Id              { get; set; }
        public long     CampaignId      { get; set; }
        public string   HomeownerPhone  { get; set; } = "";
        public string   HomeownerName   { get; set; } = "";   // from skip trace, may be empty
        public string   Address         { get; set; } = "";
        public double   Lat             { get; set; }
        public double   Lng             { get; set; }
        public double   HailSizeInches  { get; set; }
        public DateTime StormDate       { get; set; }
        public string   ResponseText    { get; set; } = "";   // homeowner's exact reply
        public DateTime RespondedAt     { get; set; } = DateTime.UtcNow;
        public string   Status          { get; set; } = "new"; // new | available | sold | expired
        public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;

        // Nav
        public LeadGenCampaign? Campaign { get; set; }
    }
}
