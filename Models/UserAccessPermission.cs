namespace BIS.ERP.Models
{
    public class UserAccessPermission
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int UserId { get; set; }
        public string NavigationKey { get; set; } = string.Empty;
        public bool IsAllowed { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
