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
            // Инициализация тестовых пользователей
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

        public User? CurrentUser => _currentUser;
        public bool IsAdmin => _currentUser?.Role == UserRole.Admin;
        public bool IsAuthenticated => _currentUser != null;

        private string HashPassword(string password)
        {
            // Для простоты используем простой хэш, в реальном проекте используйте BCrypt
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }

        public async Task<AuthResult> LoginAsync(string login, string password)
        {
            return await Task.Run(() =>
            {
                var user = _users.FirstOrDefault(u => u.Login == login);

                if (user == null)
                {
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = "Неверный логин или пароль"
                    };
                }

                if (!VerifyPassword(password, user.PasswordHash))
                {
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = "Неверный логин или пароль"
                    };
                }

                if (!user.IsActive)
                {
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = "Пользователь заблокирован"
                    };
                }

                _currentUser = user;
                user.LastLoginDate = DateTime.Now;

                return new AuthResult
                {
                    Success = true,
                    User = user
                };
            });
        }

        public async Task<bool> RegisterAsync(string login, string password, string fullName, string email)
        {
            return await Task.Run(() =>
            {
                if (_users.Any(u => u.Login == login))
                    return false;

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

                return true;
            });
        }

        public void Logout()
        {
            _currentUser = null;
        }
    }
}