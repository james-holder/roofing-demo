namespace RoofingLeadGeneration.Data.Models
{
    /// <summary>
    /// One SMS blast campaign tied to a specific storm event in TX/OK.
    /// Created manually by an admin, then fired via the internal dashboard.
    /// </summary>
    public class LeadGenCampaign
    {
        public long     Id             { get; set; }
        public string   StateAbbr      { get; set; } = "";      // TX or OK
        public DateTime StormDate      { get; set; }
        public double   HailSizeInches { get; set; }
        public double   CenterLat      { get; set; }
        public double   CenterLng      { get; set; }
        public double   RadiusMiles    { get; set; }
        public string   Status         { get; set; } = "draft"; // draft | sending | complete
        public int      TotalSent      { get; set; }
        public int      TotalResponded { get; set; }
        public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt        { get; set; }
        public string   Notes          { get; set; } = "";

        // Nav
        public List<LeadGenLead> Leads { get; set; } = new();
    }
}
