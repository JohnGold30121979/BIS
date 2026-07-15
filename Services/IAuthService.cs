using System.Threading.Tasks;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public interface IAuthService
    {
        Task<AuthResult> LoginAsync(string login, string password);
        Task<bool> RegisterAsync(string login, string password, string fullName, string email, UserRole role);
        Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword);
        void Logout();
        User? CurrentUser { get; }
        bool IsAdmin { get; }
        bool IsAuthenticated { get; }
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public User? User { get; set; }
    }
}
