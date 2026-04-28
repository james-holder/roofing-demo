namespace RoofingLeadGeneration.Data.Models
{
    public class LeadGenTarget
    {
        public long   Id         { get; set; }
        public long   CampaignId { get; set; }
        public string Phone      { get; set; } = "";
        public string Address    { get; set; } = "";
        public DateTime AddedAt  { get; set; } = DateTime.UtcNow;

        public LeadGenCampaign? Campaign { get; set; }
    }
}
