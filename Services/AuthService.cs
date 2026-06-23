using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    public class AuthService : IAuthService
    {
        private User? _currentUser;

        public User? CurrentUser => _currentUser;
        public bool IsAdmin => _currentUser?.Role == UserRole.Admin;
        public bool IsAuthenticated => _currentUser != null;

        public async Task<AuthResult> LoginAsync(string login, string password)
        {
            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new UserAccessService(context).EnsureSchemaAsync();
            var user = await context.Users.FirstOrDefaultAsync(item => item.Login == login);

            if (user == null || !VerifyPassword(password, user.PasswordHash))
                return new AuthResult { Success = false, ErrorMessage = "Неверный логин или пароль" };
            if (!user.IsActive)
                return new AuthResult { Success = false, ErrorMessage = "Пользователь заблокирован" };

            if (!IsBcryptHash(user.PasswordHash))
                user.PasswordHash = HashPassword(password);
            user.LastLoginDate = DateTime.UtcNow;
            await context.SaveChangesAsync();

            _currentUser = user;
            return new AuthResult { Success = true, User = user };
        }

        public async Task<bool> RegisterAsync(string login, string password, string fullName, string email)
        {
            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new UserAccessService(context).EnsureSchemaAsync();
            if (await context.Users.AnyAsync(user => user.Login == login))
                return false;

            await context.Users.AddAsync(new User
            {
                Login = login,
                Email = email,
                FullName = fullName,
                PasswordHash = HashPassword(password),
                Role = UserRole.User,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return true;
        }

        public void Logout()
        {
            _currentUser = null;
        }

        private static string HashPassword(string password) =>
            global::BCrypt.Net.BCrypt.HashPassword(password);

        private static bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return false;
            return IsBcryptHash(hash)
                ? global::BCrypt.Net.BCrypt.Verify(password, hash)
                : LegacyHashPassword(password) == hash;
        }

        private static bool IsBcryptHash(string hash) =>
            hash.StartsWith("$2", StringComparison.Ordinal);

        private static string LegacyHashPassword(string password) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
    }
}
