using System.Security.Cryptography;
using System.Text;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public static class UserRoleIntegrityService
    {
        private const string SignatureKey = "BIS.ERP.UserRoleIntegrity.v1";

        public static void RefreshChecksum(User user)
        {
            user.RoleChecksum = CreateChecksum(user);
        }

        public static bool HasValidChecksum(User user)
        {
            if (string.IsNullOrWhiteSpace(user.RoleChecksum))
                return false;

            var actualBytes = Encoding.UTF8.GetBytes(user.RoleChecksum);
            var expectedBytes = Encoding.UTF8.GetBytes(CreateChecksum(user));
            return actualBytes.Length == expectedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }

        private static string CreateChecksum(User user)
        {
            var payload = BuildPayload(user);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SignatureKey));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        private static string BuildPayload(User user)
        {
            var createdAt = user.CreatedAt.Kind == DateTimeKind.Utc
                ? user.CreatedAt
                : user.CreatedAt.ToUniversalTime();

            return string.Join("|",
                "v1",
                user.Id,
                NormalizeLogin(user.Login),
                (int)user.Role,
                createdAt.Ticks);
        }

        private static string NormalizeLogin(string login) =>
            (login ?? string.Empty).Trim().ToLowerInvariant();
    }
}
