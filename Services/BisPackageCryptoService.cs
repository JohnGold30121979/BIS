using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BIS.ERP.Services
{
    public static class BisPackageCryptoService
    {
        private const string EnvelopeFormat = "BIS.EncryptedPackage";
        private const int EnvelopeVersion = 1;
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 200_000;

        // Первый уровень защиты поставки. В промышленной поставке этот секрет можно заменить лицензионным ключом клиента.
        private const string DefaultSecret = "BIS.ERP|EncryptedPackage|2026-07|v1";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static byte[] Protect(byte[] plainData, string kind, string? secret = null)
        {
            if (plainData.Length == 0)
                throw new ArgumentException("Нет данных для шифрования.", nameof(plainData));
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Не указан тип пакета.", nameof(kind));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = DeriveKey(secret, salt);
            var cipherText = new byte[plainData.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plainData, cipherText, tag, BuildAad(kind));
            }

            var envelope = new EncryptedPackageEnvelope
            {
                Kind = kind,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                CipherText = Convert.ToBase64String(cipherText),
                PlainSha256 = ComputeSha256(plainData),
                CreatedAt = DateTime.UtcNow
            };

            return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        }

        public static byte[] Unprotect(byte[] packageData, string expectedKind, string? secret = null)
        {
            if (!IsEncryptedPackage(packageData))
                throw new InvalidOperationException("Файл не является зашифрованным пакетом BIS.");

            var envelope = JsonSerializer.Deserialize<EncryptedPackageEnvelope>(packageData, JsonOptions)
                ?? throw new InvalidOperationException("Зашифрованный пакет поврежден.");

            if (!EnvelopeFormat.Equals(envelope.Format, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Неверный формат зашифрованного пакета.");
            if (envelope.Version != EnvelopeVersion)
                throw new InvalidOperationException($"Версия зашифрованного пакета {envelope.Version} не поддерживается.");
            if (!expectedKind.Equals(envelope.Kind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Пакет имеет тип '{envelope.Kind}', ожидался '{expectedKind}'.");

            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var cipherText = Convert.FromBase64String(envelope.CipherText);
            var plainData = new byte[cipherText.Length];
            var key = DeriveKey(secret, salt);

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipherText, tag, plainData, BuildAad(envelope.Kind));
            }

            var actualHash = ComputeSha256(plainData);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(envelope.PlainSha256)))
            {
                throw new InvalidOperationException("Контрольная сумма пакета не совпадает.");
            }

            return plainData;
        }

        public static bool IsEncryptedPackage(byte[] packageData)
        {
            try
            {
                using var document = JsonDocument.Parse(packageData);
                return document.RootElement.TryGetProperty(nameof(EncryptedPackageEnvelope.Format), out var format) &&
                       EnvelopeFormat.Equals(format.GetString(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string ComputeSha256(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static byte[] DeriveKey(string? secret, byte[] salt)
        {
            var source = string.IsNullOrWhiteSpace(secret) ? DefaultSecret : secret;
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(source),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);
        }

        private static byte[] BuildAad(string kind) =>
            Encoding.UTF8.GetBytes($"{EnvelopeFormat}|{EnvelopeVersion}|{kind}");

        private sealed class EncryptedPackageEnvelope
        {
            public string Format { get; set; } = EnvelopeFormat;
            public int Version { get; set; } = EnvelopeVersion;
            public string Kind { get; set; } = string.Empty;
            public string Kdf { get; set; } = "PBKDF2-SHA256";
            public int Iterations { get; set; } = BisPackageCryptoService.Iterations;
            public string Salt { get; set; } = string.Empty;
            public string Nonce { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public string CipherText { get; set; } = string.Empty;
            public string PlainSha256 { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }
    }
}
