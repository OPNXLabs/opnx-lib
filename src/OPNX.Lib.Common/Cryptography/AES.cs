using OPNX.Lib.Common.Logging;
using System.Security.Cryptography;
using System.Text;

namespace OPNX.Lib.Common.Cryptography
{
    public class AES
    {
        // AES-256 암호화 (CBC 모드, 고정 IV)
        public static string Encrypt256(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = new byte[16]; // 0으로 초기화된 고정 IV

                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    cs.Write(inputBytes, 0, inputBytes.Length);
                }

                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }

        // AES-256 복호화 (CBC 모드, 고정 IV)
        public static string Decrypt256(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = new byte[16]; // 0으로 초기화된 고정 IV

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                {
                    byte[] inputBytes = Convert.FromBase64String(input);
                    cs.Write(inputBytes, 0, inputBytes.Length);
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }

        // AES-128 암호화 (PBKDF 사용)
        public static string Encrypt128(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using Aes aes = Aes.Create();
                byte[] plainText = Encoding.Unicode.GetBytes(input);
                byte[] salt = Encoding.ASCII.GetBytes(key.Length.ToString());

                // .NET 10.0 권장: Pbkdf2 정적 메서드 사용
                byte[] keyBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password: key,
                    salt: salt,
                    iterations: 100000,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: 32);

                byte[] ivBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password: key,
                    salt: salt,
                    iterations: 100000,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: 16);

                aes.Key = keyBytes;
                aes.IV = ivBytes;

                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(plainText, 0, plainText.Length);
                    cs.FlushFinalBlock();
                }

                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }

        // AES-128 복호화 (PBKDF 사용)
        public static string Decrypt128(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using Aes aes = Aes.Create();
                byte[] encryptedData = Convert.FromBase64String(input);
                byte[] salt = Encoding.ASCII.GetBytes(key.Length.ToString());

                // .NET 10.0 권장: Pbkdf2 정적 메서드 사용
                byte[] keyBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password: key,
                    salt: salt,
                    iterations: 100000,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: 32);

                byte[] ivBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password: key,
                    salt: salt,
                    iterations: 100000,
                    hashAlgorithm: HashAlgorithmName.SHA256,
                    outputLength: 16);

                aes.Key = keyBytes;
                aes.IV = ivBytes;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(encryptedData);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var reader = new StreamReader(cs, Encoding.Unicode);

                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }

        // ========== 보안 강화 버전 (랜덤 IV 사용) ==========

        /// <summary>
        /// AES-256 암호화 (보안 강화 버전 - 랜덤 IV)
        /// IV가 암호문 앞에 포함되어 반환됩니다.
        /// </summary>
        public static string Encrypt256Ex(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // 키를 정확히 32바이트로 맞춤
                byte[] keyBytes = new byte[32];
                byte[] sourceKey = Encoding.UTF8.GetBytes(key);
                Array.Copy(sourceKey, keyBytes, Math.Min(sourceKey.Length, 32));
                aes.Key = keyBytes;

                // 랜덤 IV 생성
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();

                // IV를 암호문 앞에 추가
                ms.Write(aes.IV, 0, aes.IV.Length);

                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    cs.Write(inputBytes, 0, inputBytes.Length);
                }

                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// AES-256 복호화 (보안 강화 버전 - 랜덤 IV)
        /// 암호문 앞의 IV를 추출하여 복호화합니다.
        /// </summary>
        public static string Decrypt256Ex(string input, string key)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                byte[] buffer = Convert.FromBase64String(input);

                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // 키를 정확히 32바이트로 맞춤
                byte[] keyBytes = new byte[32];
                byte[] sourceKey = Encoding.UTF8.GetBytes(key);
                Array.Copy(sourceKey, keyBytes, Math.Min(sourceKey.Length, 32));
                aes.Key = keyBytes;

                // IV 추출 (처음 16바이트)
                byte[] iv = new byte[16];
                Array.Copy(buffer, 0, iv, 0, 16);
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(buffer, 16, buffer.Length - 16);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var reader = new StreamReader(cs, Encoding.UTF8);

                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return string.Empty;
            }
        }
    }
}
