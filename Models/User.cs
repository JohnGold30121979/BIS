namespace BIS.ERP.Models
{
    public enum UserRole
    {
        User = 0,
        Admin = 1,
        Accountant = 2,
        Cashier = 3
    }

    public class User
    {
        public int Id { get; set; }
        public string Login { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.User;
        public string RoleChecksum { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public DateTime? LastLoginDate { get; set; }
        public Guid? LastInfoBaseId { get; set; }
    }
}
