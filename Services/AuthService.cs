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
            var normalizedLogin = NormalizeLogin(login);
            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new UserAccessService(context).EnsureSchemaAsync();
            var user = await context.Users.FirstOrDefaultAsync(item => item.Login.ToLower() == normalizedLogin);

            if (user == null)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Пользователь '{normalizedLogin}' не найден в текущей инфобазе.");
                return new AuthResult { Success = false, ErrorMessage = "Неверный логин или пароль" };
            }

            var hasRoleChecksum = !string.IsNullOrWhiteSpace(user.RoleChecksum);
            if (hasRoleChecksum && !UserRoleIntegrityService.HasValidChecksum(user))
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Контрольная сумма роли пользователя '{normalizedLogin}' не совпала.");
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Роль пользователя была изменена вне системы. Обратитесь к администратору."
                };
            }

            // Проверка пароля
            bool passwordValid = false;
            try
            {
                passwordValid = VerifyPassword(password, user.PasswordHash);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Ошибка проверки пароля для '{login}': {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AuthService] Хэш в БД: '{user.PasswordHash}'");
                System.Diagnostics.Debug.WriteLine($"[AuthService] Длина хэша: {user.PasswordHash?.Length ?? 0}");
                return new AuthResult { Success = false, ErrorMessage = "Внутренняя ошибка аутентификации" };
            }

            if (!passwordValid)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Пароль для пользователя '{normalizedLogin}' не прошел проверку.");
                return new AuthResult { Success = false, ErrorMessage = "Неверный логин или пароль" };
            }

            if (user.Role != UserRole.Admin && !user.IsActive)
                return new AuthResult { Success = false, ErrorMessage = "Пользователь заблокирован" };

            if (!hasRoleChecksum)
                UserRoleIntegrityService.RefreshChecksum(user);
            if (!IsBcryptHash(user.PasswordHash))
                user.PasswordHash = HashPassword(password);
            user.LastLoginDate = DateTime.UtcNow;
            await context.SaveChangesAsync();

            _currentUser = user;
            return new AuthResult { Success = true, User = user };
        }

        public async Task<bool> RegisterAsync(string login, string password, string fullName, string email, UserRole role)
        {
            var normalizedLogin = NormalizeLogin(login);
            var safeRole = role is UserRole.Accountant or UserRole.Cashier ? role : UserRole.User;
            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new UserAccessService(context).EnsureSchemaAsync();
            if (await context.Users.AnyAsync(user => user.Login.ToLower() == normalizedLogin))
                return false;

            string passwordHash;
            try
            {
                passwordHash = HashPassword(password);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Ошибка хэширования пароля: {ex.Message}");
                throw;
            }

            System.Diagnostics.Debug.WriteLine($"[AuthService] Регистрация пользователя '{normalizedLogin}'");
            System.Diagnostics.Debug.WriteLine($"[AuthService]   Password length: {password?.Length ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[AuthService]   Hash: {passwordHash}");

            var user = new User
            {
                Login = normalizedLogin,
                Email = email,
                FullName = fullName,
                PasswordHash = passwordHash,
                Role = safeRole,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            UserRoleIntegrityService.RefreshChecksum(user);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            if (_currentUser == null)
                return new AuthResult { Success = false, ErrorMessage = "Пользователь не авторизован" };

            if (string.IsNullOrWhiteSpace(currentPassword))
                return new AuthResult { Success = false, ErrorMessage = "Введите текущий пароль" };

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return new AuthResult { Success = false, ErrorMessage = "Новый пароль должен быть не менее 6 символов" };

            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new UserAccessService(context).EnsureSchemaAsync();

            var user = await context.Users.FirstOrDefaultAsync(item => item.Id == _currentUser.Id);
            if (user == null)
                return new AuthResult { Success = false, ErrorMessage = "Пользователь не найден в текущей инфобазе" };

            var hasRoleChecksum = !string.IsNullOrWhiteSpace(user.RoleChecksum);
            if (hasRoleChecksum && !UserRoleIntegrityService.HasValidChecksum(user))
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Роль пользователя была изменена вне системы. Смена пароля заблокирована."
                };

            bool currentPasswordValid;
            try
            {
                currentPasswordValid = VerifyPassword(currentPassword, user.PasswordHash);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Ошибка проверки текущего пароля: {ex.Message}");
                return new AuthResult { Success = false, ErrorMessage = "Внутренняя ошибка проверки пароля" };
            }

            if (!currentPasswordValid)
                return new AuthResult { Success = false, ErrorMessage = "Текущий пароль указан неверно" };

            if (!hasRoleChecksum)
                UserRoleIntegrityService.RefreshChecksum(user);
            user.PasswordHash = HashPassword(newPassword);
            await context.SaveChangesAsync();

            _currentUser.PasswordHash = user.PasswordHash;
            return new AuthResult { Success = true, User = _currentUser };
        }

        public void Logout()
        {
            _currentUser = null;
        }

        private static string HashPassword(string password) =>
            global::BCrypt.Net.BCrypt.HashPassword(password);

        private static string NormalizeLogin(string login) =>
            (login ?? string.Empty).Trim().ToLowerInvariant();

        private static bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return false;
            if (string.IsNullOrWhiteSpace(password))
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
