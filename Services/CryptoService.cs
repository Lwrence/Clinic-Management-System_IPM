using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using CruzNeryClinic.Data;

namespace CruzNeryClinic.Services
{
    public static class CryptoService
    {
        #region Constants

        private const int KeySizeBytes = 32;   // 256-bit AES key
        private const int NonceSizeBytes = 12; // Recommended GCM nonce size
        private const int TagSizeBytes = 16;   // 128-bit authentication tag

        private const string EncryptedPrefix = "ENC:";

        #endregion

        #region Public Methods

        public static string EncryptString(string? plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return string.Empty;

            // Prevent double encryption if the value is already encrypted.
            if (plainText.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                return plainText;

            byte[] key = GetOrCreateKey();
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSizeBytes];

            using AesGcm aesGcm = new(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            byte[] payload = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];

            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);

            return EncryptedPrefix + Convert.ToBase64String(payload);
        }

        public static string DecryptString(string? encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                return string.Empty;

            // Allows older plain text records to still load safely.
            if (!encryptedText.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                return encryptedText;

            try
            {
                string base64Payload = encryptedText.Substring(EncryptedPrefix.Length);
                byte[] payload = Convert.FromBase64String(base64Payload);

                if (payload.Length < NonceSizeBytes + TagSizeBytes)
                    return string.Empty;

                byte[] nonce = new byte[NonceSizeBytes];
                byte[] tag = new byte[TagSizeBytes];
                byte[] cipherBytes = new byte[payload.Length - NonceSizeBytes - TagSizeBytes];

                Buffer.BlockCopy(payload, 0, nonce, 0, NonceSizeBytes);
                Buffer.BlockCopy(payload, NonceSizeBytes, tag, 0, TagSizeBytes);
                Buffer.BlockCopy(payload, NonceSizeBytes + TagSizeBytes, cipherBytes, 0, cipherBytes.Length);

                byte[] key = GetOrCreateKey();
                byte[] plainBytes = new byte[cipherBytes.Length];

                using AesGcm aesGcm = new(key, TagSizeBytes);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // If the key changes or data is corrupted, avoid crashing the whole patient screen.
                return "[Unable to decrypt]";
            }
        }

        public static string EncryptDecimal(decimal value)
        {
            return EncryptString(value.ToString(CultureInfo.InvariantCulture));
        }

        public static decimal DecryptDecimal(string? encryptedText, decimal fallback = 0m)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                return fallback;

            string decryptedText = DecryptString(encryptedText);

            if (decimal.TryParse(
                    decryptedText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal result))
            {
                return result;
            }

            return fallback;
        }

        public static string EncryptInt(int value)
        {
            return EncryptString(value.ToString(CultureInfo.InvariantCulture));
        }

        public static int DecryptInt(string? encryptedText, int fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                return fallback;

            string decryptedText = DecryptString(encryptedText);

            if (int.TryParse(
                    decryptedText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out int result))
            {
                return result;
            }

            return fallback;
        }
        
        public static byte[] ExportRawKeyForBackup()
        {
            return GetOrCreateKey();
        }

        public static void ImportRawKeyFromBackup(byte[] rawKey)
        {
            if (rawKey == null || rawKey.Length != KeySizeBytes)
                throw new InvalidOperationException("Invalid AES key size.");

            if (DatabaseService.IsSharedDatabase)
                SaveSharedKey(GetSharedKeyFilePath(), rawKey);
            else
                SaveLocalKey(GetLocalKeyFilePath(), rawKey);
        }
        #endregion

        #region Key Management

        private static byte[] GetOrCreateKey()
        {
            // LAN-shared mode: the key lives next to the shared database so every
            // laptop encrypts/decrypts with the same key. It is stored as raw bytes
            // (not DPAPI-protected) because DPAPI keys are tied to one machine/user
            // and would not be portable across the laptops.
            if (DatabaseService.IsSharedDatabase)
            {
                string sharedKeyPath = GetSharedKeyFilePath();

                if (File.Exists(sharedKeyPath))
                    return LoadSharedKey(sharedKeyPath);

                // First run in shared mode: seed the shared key from this machine's
                // existing local key (so records it already created stay decryptable).
                // Run the SERVER first after updating so its data is preserved.
                string localKeyPath = GetLocalKeyFilePath();
                byte[] seedKey = File.Exists(localKeyPath)
                    ? LoadLocalKey(localKeyPath)
                    : RandomNumberGenerator.GetBytes(KeySizeBytes);

                SaveSharedKey(sharedKeyPath, seedKey);
                return seedKey;
            }

            // Standalone (default) mode: keep the secure per-user DPAPI key as before.
            string keyFilePath = GetLocalKeyFilePath();

            if (File.Exists(keyFilePath))
                return LoadLocalKey(keyFilePath);

            byte[] key = RandomNumberGenerator.GetBytes(KeySizeBytes);
            SaveLocalKey(keyFilePath, key);

            return key;
        }

        private static void SaveLocalKey(string keyFilePath, byte[] key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);

            byte[] protectedKey = ProtectedData.Protect(
                key,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            File.WriteAllBytes(keyFilePath, protectedKey);
        }

        private static byte[] LoadLocalKey(string keyFilePath)
        {
            byte[] protectedKey = File.ReadAllBytes(keyFilePath);

            return ProtectedData.Unprotect(
                protectedKey,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
        }

        private static void SaveSharedKey(string keyFilePath, byte[] key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);

            // Raw bytes so any laptop reading the shared folder uses the identical key.
            File.WriteAllBytes(keyFilePath, key);
        }

        private static byte[] LoadSharedKey(string keyFilePath)
        {
            byte[] key = File.ReadAllBytes(keyFilePath);

            if (key.Length != KeySizeBytes)
                throw new InvalidOperationException("Shared encryption key is invalid or corrupted.");

            return key;
        }

        private static string GetLocalKeyFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(
                appDataPath,
                "CruzNeryClinic",
                "Security",
                "patient_data.key"
            );
        }

        private static string GetSharedKeyFilePath()
        {
            // Sits in the same shared folder as the database (e.g. C:\ClinicDB on the
            // server / \\SERVER\ClinicDB on the client).
            return Path.Combine(DatabaseService.DatabaseDirectory, "patient_data.key");
        }

        #endregion
    }
}