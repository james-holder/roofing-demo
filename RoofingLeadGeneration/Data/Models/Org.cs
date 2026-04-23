namespace RoofingLeadGeneration.Data.Models
{
    public class Org
    {
        public long     Id        { get; set; }
        public string   Name      { get; set; } = "";
        public long?    OwnerId   { get; set; }
        /// <summary>free | pro | agency</summary>
        public string   Plan      { get; set; } = "free";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User?                    Owner   { get; set; }
        public ICollection<User>        Members { get; set; } = new List<User>();
        public ICollection<OrgInvite>   Invites { get; set; } = new List<OrgInvite>();
    }
}
