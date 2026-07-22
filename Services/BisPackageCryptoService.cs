using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BIS.ERP.Services
{
    public static class BisPackageCryptoService
    {
        private const string EnvelopeFormat = "BIS.EncryptedPackage";
        private const int EnvelopeVersion = 1;
        private const int BinaryEnvelopeVersion = 2;
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 200_000;
        private static readonly byte[] BinaryMagic = Encoding.ASCII.GetBytes("BISPKG2\0");

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
            var key = DeriveKey(secret, salt, Iterations);
            var cipherText = new byte[plainData.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plainData, cipherText, tag, BuildAad(kind, BinaryEnvelopeVersion));
            }

            return BuildBinaryEnvelope(kind, salt, nonce, tag, cipherText, ComputeSha256(plainData));
        }

        public static byte[] Unprotect(byte[] packageData, string expectedKind, string? secret = null)
        {
            if (IsBinaryEncryptedPackage(packageData))
                return UnprotectBinary(packageData, expectedKind, secret);

            if (!IsJsonEncryptedPackage(packageData))
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
            var key = DeriveKey(secret, salt, envelope.Iterations > 0 ? envelope.Iterations : Iterations);

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipherText, tag, plainData, BuildAad(envelope.Kind, envelope.Version));
            }

            ValidatePlainHash(plainData, envelope.PlainSha256);
            return plainData;
        }

        public static bool IsEncryptedPackage(byte[] packageData) =>
            IsBinaryEncryptedPackage(packageData) || IsJsonEncryptedPackage(packageData);

        public static string ComputeSha256(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static byte[] BuildBinaryEnvelope(
            string kind,
            byte[] salt,
            byte[] nonce,
            byte[] tag,
            byte[] cipherText,
            string plainSha256)
        {
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
            writer.Write(BinaryMagic);
            writer.Write(BinaryEnvelopeVersion);
            writer.Write(Iterations);
            WriteByteBlock(writer, Encoding.UTF8.GetBytes(kind));
            WriteByteBlock(writer, salt);
            WriteByteBlock(writer, nonce);
            WriteByteBlock(writer, tag);
            WriteByteBlock(writer, Encoding.ASCII.GetBytes(plainSha256));
            writer.Write((long)cipherText.Length);
            writer.Write(cipherText);
            writer.Flush();
            return memory.ToArray();
        }

        private static byte[] UnprotectBinary(byte[] packageData, string expectedKind, string? secret)
        {
            using var memory = new MemoryStream(packageData, writable: false);
            using var reader = new BinaryReader(memory, Encoding.UTF8, leaveOpen: false);

            var magic = reader.ReadBytes(BinaryMagic.Length);
            if (!magic.AsSpan().SequenceEqual(BinaryMagic))
                throw new InvalidOperationException("Неверный заголовок зашифрованного пакета.");

            var version = reader.ReadInt32();
            if (version != BinaryEnvelopeVersion)
                throw new InvalidOperationException($"Версия бинарного зашифрованного пакета {version} не поддерживается.");

            var iterations = reader.ReadInt32();
            if (iterations <= 0)
                throw new InvalidOperationException("В зашифрованном пакете указаны неверные параметры KDF.");

            var kind = Encoding.UTF8.GetString(ReadByteBlock(reader, 1024, "тип пакета"));
            if (!expectedKind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Пакет имеет тип '{kind}', ожидался '{expectedKind}'.");

            var salt = ReadByteBlock(reader, 128, "salt");
            var nonce = ReadByteBlock(reader, 64, "nonce");
            var tag = ReadByteBlock(reader, 64, "tag");
            var plainSha256 = Encoding.ASCII.GetString(ReadByteBlock(reader, 128, "контрольная сумма"));
            var cipherLength = reader.ReadInt64();
            var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (cipherLength <= 0 || cipherLength > remaining || cipherLength > int.MaxValue)
                throw new InvalidOperationException("Размер зашифрованных данных в пакете некорректен.");

            var cipherText = reader.ReadBytes((int)cipherLength);
            if (cipherText.LongLength != cipherLength)
                throw new InvalidOperationException("Зашифрованный пакет поврежден: данные прочитаны не полностью.");

            var plainData = new byte[cipherText.Length];
            var key = DeriveKey(secret, salt, iterations);
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipherText, tag, plainData, BuildAad(kind, version));
            }

            ValidatePlainHash(plainData, plainSha256);
            return plainData;
        }

        private static void WriteByteBlock(BinaryWriter writer, byte[] value)
        {
            writer.Write(value.Length);
            writer.Write(value);
        }

        private static byte[] ReadByteBlock(BinaryReader reader, int maxLength, string fieldName)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > maxLength)
                throw new InvalidOperationException($"Некорректная длина поля '{fieldName}' в зашифрованном пакете.");

            var value = reader.ReadBytes(length);
            if (value.Length != length)
                throw new InvalidOperationException($"Зашифрованный пакет поврежден: поле '{fieldName}' прочитано не полностью.");

            return value;
        }

        private static bool IsBinaryEncryptedPackage(byte[] packageData) =>
            packageData.Length >= BinaryMagic.Length &&
            packageData.AsSpan(0, BinaryMagic.Length).SequenceEqual(BinaryMagic);

        private static bool IsJsonEncryptedPackage(byte[] packageData)
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

        private static void ValidatePlainHash(byte[] plainData, string expectedHash)
        {
            var actualHash = ComputeSha256(plainData);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(expectedHash)))
            {
                throw new InvalidOperationException("Контрольная сумма пакета не совпадает.");
            }
        }

        private static byte[] DeriveKey(string? secret, byte[] salt, int iterations)
        {
            var source = string.IsNullOrWhiteSpace(secret) ? DefaultSecret : secret;
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(source),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                KeySize);
        }

        private static byte[] BuildAad(string kind, int version) =>
            Encoding.UTF8.GetBytes($"{EnvelopeFormat}|{version}|{kind}");

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