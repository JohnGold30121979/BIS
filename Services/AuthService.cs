using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class AuthService : IAuthService
    {
        private User? _currentUser;
        private readonly List<User> _users = new();

        public AuthService()
        {
            if (!_users.Any())
            {
                _users.Add(new User
                {
                    Id = 1,
                    Login = "admin",
                    FullName = "Администратор",
                    PasswordHash = HashPassword("admin"),
                    Email = "admin@test.com",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                _users.Add(new User
                {
                    Id = 2,
                    Login = "user",
                    FullName = "Пользователь",
                    PasswordHash = HashPassword("user"),
                    Email = "user@test.com",
                    Role = UserRole.User,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        public User? CurrentUser => _currentUser;
        public bool IsAdmin => _currentUser?.Role == UserRole.Admin;
        public bool IsAuthenticated => _currentUser != null;

        private string HashPassword(string password)
        {
            return global::BCrypt.Net.BCrypt.HashPassword(password);
        }

        private bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return false;

            return IsBcryptHash(hash)
                ? global::BCrypt.Net.BCrypt.Verify(password, hash)
                : LegacyHashPassword(password) == hash;
        }

        private static bool IsBcryptHash(string hash)
        {
            return hash.StartsWith("$2", StringComparison.Ordinal);
        }

        private static string LegacyHashPassword(string password)
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
        }

        public Task<AuthResult> LoginAsync(string login, string password)
        {
            var user = _users.FirstOrDefault(u => u.Login == login);

            if (user == null)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный логин или пароль"
                });
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный логин или пароль"
                });
            }

            if (!user.IsActive)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Пользователь заблокирован"
                });
            }

            if (!IsBcryptHash(user.PasswordHash))
            {
                user.PasswordHash = HashPassword(password);
            }

            _currentUser = user;
            user.LastLoginDate = DateTime.UtcNow;

            return Task.FromResult(new AuthResult
            {
                Success = true,
                User = user
            });
        }

        public Task<bool> RegisterAsync(string login, string password, string fullName, string email)
        {
            if (_users.Any(u => u.Login == login))
                return Task.FromResult(false);

            _users.Add(new User
            {
                Id = _users.Count + 1,
                Login = login,
                Email = email,
                FullName = fullName,
                PasswordHash = HashPassword(password),
                Role = UserRole.User,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            return Task.FromResult(true);
        }

        public void Logout()
        {
            _currentUser = null;
        }
    }
}
