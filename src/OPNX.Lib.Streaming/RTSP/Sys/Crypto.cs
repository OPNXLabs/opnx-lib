using OPNX.Lib.Common.Logging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OPNX.Lib.Streaming.RTSP.Sys
{
    public class Crypto
    {
        // TODO: When .NET Standard and Framework support are deprecated these pragmas can be removed.

        public const int DEFAULT_RANDOM_LENGTH = 10;    // Number of digits to return for default random numbers.
        public const int AES_KEY_SIZE = 32;
        public const int AES_IV_SIZE = 16;
        private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        //private readonly static ILogger logger = Log.ForContext<Crypto>(); // NullLoggerFactory.Instance.CreateLogger("Crypto_Logger");

        static int seed = Environment.TickCount;

        static readonly ThreadLocal<Random> random =
            new(() => new Random(Interlocked.Increment(ref seed)));

        public static int Rand()
        {
            return random.Value.Next();
        }

        public static int Rand(int maxValue)
        {
            return random.Value.Next(maxValue);
        }

        private static readonly RandomNumberGenerator m_randomProvider = RandomNumberGenerator.Create();

        public static string SymmetricEncrypt(string key, string iv, string plainText)
        {
            if (plainText.IsNullOrBlank())
            {
                throw new ApplicationException("The plain text string cannot be empty in SymmetricEncrypt.");
            }

            return SymmetricEncrypt(key, iv, Encoding.UTF8.GetBytes(plainText));
        }

        public static string SymmetricEncrypt(string key, string iv, byte[] plainTextBytes)
        {
            if (key.IsNullOrBlank())
            {
                throw new ApplicationException("The key string cannot be empty in SymmetricEncrypt.");
            }
            else if (iv.IsNullOrBlank())
            {
                throw new ApplicationException("The initialisation vector cannot be empty in SymmetricEncrypt.");
            }
            else if (plainTextBytes == null)
            {
                throw new ApplicationException("The plain text string cannot be empty in SymmetricEncrypt.");
            }

            using Aes aes = Aes.Create();
            ICryptoTransform encryptor = aes.CreateEncryptor(GetFixedLengthByteArray(key, AES_KEY_SIZE), GetFixedLengthByteArray(iv, AES_IV_SIZE));

            MemoryStream resultStream = new();
            CryptoStream cryptoStream = new(resultStream, encryptor, CryptoStreamMode.Write);
            cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
            cryptoStream.Close();

            return Convert.ToBase64String(resultStream.ToArray());
        }

        public static string SymmetricDecrypt(string key, string iv, string cipherText)
        {
            if (cipherText.IsNullOrBlank())
            {
                throw new ApplicationException("The cipher text string cannot be empty in SymmetricDecrypt.");
            }

            return SymmetricDecrypt(key, iv, Convert.FromBase64String(cipherText));
        }

        public static string SymmetricDecrypt(string key, string iv, byte[] cipherBytes)
        {
            if (key.IsNullOrBlank())
            {
                throw new ApplicationException("The key string cannot be empty in SymmetricDecrypt.");
            }
            else if (iv.IsNullOrBlank())
            {
                throw new ApplicationException("The initialisation vector cannot be empty in SymmetricDecrypt.");
            }
            else if (cipherBytes == null)
            {
                throw new ApplicationException("The cipher byte array cannot be empty in SymmetricDecrypt.");
            }

            using Aes aes = Aes.Create();
            ICryptoTransform encryptor = aes.CreateDecryptor(GetFixedLengthByteArray(key, AES_KEY_SIZE), GetFixedLengthByteArray(iv, AES_IV_SIZE));

            MemoryStream cipherStream = new(cipherBytes);
            CryptoStream cryptoStream = new(cipherStream, encryptor, CryptoStreamMode.Read);
            StreamReader cryptoStreamReader = new(cryptoStream);
            return cryptoStreamReader.ReadToEnd();
        }

        private static byte[] GetFixedLengthByteArray(string value, int length)
        {
            if (value.Length < length)
            {
                while (value.Length < length)
                {
                    value += 0x00;
                }
            }
            else if (value.Length > length)
            {
                value = value[..length];
            }

            return Encoding.UTF8.GetBytes(value);
        }

        public static string GetRandomString(int length)
        {
            char[] buffer = new char[length];

            for (int i = 0; i < length; i++)
            {
                buffer[i] = CHARS[Rand(CHARS.Length)];
            }
            return new string(buffer);
        }

        public static string GetRandomString()
        {
            return GetRandomString(DEFAULT_RANDOM_LENGTH);
        }

        /// <summary>
        /// Returns a 10 digit random number.
        /// </summary>
        /// <returns></returns>
        public static int GetRandomInt()
        {
            return GetRandomInt(DEFAULT_RANDOM_LENGTH);
        }

        /// <summary>
        /// Returns a random number of a specified length.
        /// </summary>
        public static int GetRandomInt(int length)
        {
            int randomStart = 1000000000;
            int randomEnd = Int32.MaxValue;

            if (length > 0 && length < DEFAULT_RANDOM_LENGTH)
            {
                randomStart = Convert.ToInt32(Math.Pow(10, length - 1));
                randomEnd = Convert.ToInt32(Math.Pow(10, length) - 1);
            }

            return GetRandomInt(randomStart, randomEnd);
        }

        public static Int32 GetRandomInt(Int32 minValue, Int32 maxValue)
        {

            if (minValue > maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(minValue));
            }
            else if (minValue == maxValue)
            {
                return minValue;
            }

            Int64 diff = maxValue - minValue + 1;
            int attempts = 0;
            while (attempts < 10)
            {
                byte[] uint32Buffer = new byte[4];
                m_randomProvider.GetBytes(uint32Buffer);
                UInt32 rand = BitConverter.ToUInt32(uint32Buffer, 0);

                Int64 max = (1 + (Int64)UInt32.MaxValue);
                Int64 remainder = max % diff;
                if (rand <= max - remainder)
                {
                    return (Int32)(minValue + (rand % diff));
                }
                attempts++;
            }
            throw new ApplicationException("GetRandomInt did not return an appropriate random number within 10 attempts.");
        }

        public static UInt16 GetRandomUInt16()
        {
            byte[] uint16Buffer = new byte[2];
            m_randomProvider.GetBytes(uint16Buffer);
            return BitConverter.ToUInt16(uint16Buffer, 0);
        }

        public static UInt32 GetRandomUInt(bool noZero = false)
        {
            byte[] uint32Buffer = new byte[4];
            m_randomProvider.GetBytes(uint32Buffer);
            var randomUint = BitConverter.ToUInt32(uint32Buffer, 0);

            if (noZero && randomUint == 0)
            {
                m_randomProvider.GetBytes(uint32Buffer);
                randomUint = BitConverter.ToUInt32(uint32Buffer, 0);
            }

            return randomUint;
        }

        public static UInt64 GetRandomULong()
        {
            byte[] uint64Buffer = new byte[8];
            m_randomProvider.GetBytes(uint64Buffer);
            return BitConverter.ToUInt64(uint64Buffer, 0);
        }

        public static byte[] CreateRandomSalt(int length)
        {
            byte[] randBytes = new byte[length];
            m_randomProvider.GetBytes(randBytes);
            return randBytes;
        }

        /// <summary>
        /// This version reads the whole file in at once. This is not great since it can consume
        /// a lot of memory if the file is large. However a buffered approach generates
        /// different hashes across different platforms.
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public static string GetHash(string filepath)
        {
            ArgumentException.ThrowIfNullOrEmpty(filepath);

            using FileStream stream = GetFileStream(filepath);

            byte[] hash = SHA1.HashData(stream);

            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Used by methods wishing to perform a hash operation on a file. This method
        /// will perform a number of checks and if happy return a read only file stream.
        /// </summary>
        /// <param name="filepath">The path to the input file for the hash operation.</param>
        /// <returns>A read only file stream for the file or throws an exception if there is a problem.</returns>
        private static FileStream GetFileStream(string filepath)
        {
            // Check that the file exists.
            if (!File.Exists(filepath))
            {
                LogManager.Error("Cannot open a non-existent file for a hash operation, " + filepath + ".");
                throw new IOException("Cannot open a non-existent file for a hash operation, " + filepath + ".");
            }

            // Open the file.
            FileStream inputStream = File.OpenRead(filepath);

            if (inputStream.Length == 0)
            {
                inputStream.Close();
                LogManager.Error("Cannot perform a hash operation on an empty file, " + filepath + ".");
                throw new IOException("Cannot perform a hash operation on an empty file, " + filepath + ".");
            }

            return inputStream;
        }

        /// <summary>
        /// Gets an "X2" string representation of a random number.
        /// </summary>
        /// <param name="byteLength">The byte length of the random number string to obtain.</param>
        /// <returns>A string representation of the random number. It will be twice the length of byteLength.</returns>
        public static string GetRandomByteString(int byteLength)
        {
            byte[] myKey = new byte[byteLength];
            m_randomProvider.GetBytes(myKey);
            string sessionID = null;
            myKey.ToList().ForEach(b => sessionID += b.ToString("x2"));
            return sessionID;
        }

        /// <summary>
        /// Fills a buffer with random bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill.</param>
        public static void GetRandomBytes(byte[] buffer)
        {
            m_randomProvider.GetBytes(buffer);
        }

        public static byte[] GetSHAHash(params string[] values)
        {
            ArgumentNullException.ThrowIfNull(values);

            // values 전체를 UTF8 바이트로 연결
            byte[] data = Encoding.UTF8.GetBytes(string.Concat(values));

            return SHA1.HashData(data);
        }

        public static string GetSHAHashAsString(params string[] values)
        {
            return Convert.ToBase64String(GetSHAHash(values));
        }

        /// <summary>
        /// Returns the hash with each byte as an X2 string. This is useful for situations where
        /// the hash needs to only contain safe ASCII characters.
        /// </summary>
        /// <param name="values">The list of string to concatenate and hash.</param>
        /// <returns>A string with "safe" (0-9 and A-F) characters representing the hash.</returns>
        public static string GetSHAHashAsHex(params string[] values)
        {
            byte[] hash = GetSHAHash(values);
            string hashStr = null;
            hash.ToList().ForEach(b => hashStr += b.ToString("x2"));
            return hashStr;
        }

        /// <summary>
        /// Gets the HSA256 hash of an arbitrary buffer.
        /// </summary>
        /// <param name="buffer">The buffer to hash.</param>
        /// <returns>A hex string representing the hashed buffer.</returns>
        public static string GetSHA256Hash(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            return SHA256.HashData(buffer).HexStr();
        }

        /// <summary>
        /// Attempts to load an X509 certificate from a Windows OS certificate store.
        /// </summary>
        /// <param name="storeLocation">The certificate store to load from, can be CurrentUser or LocalMachine.</param>
        /// <param name="certificateSubject">The subject name of the certificate to attempt to load.</param>
        /// <param name="checkValidity">Checks if the certificate is current and has a verifiable certificate issuer list. Should be
        /// set to false for self issued certificates.</param>
        /// <returns>A certificate object if the load is successful otherwise null.</returns>
        public static X509Certificate2 LoadCertificate(StoreLocation storeLocation, string certificateSubject, bool checkValidity)
        {
            X509Store store = new(storeLocation);
            LogManager.Debug("Certificate store " + store.Location + " opened");
            store.Open(OpenFlags.OpenExistingOnly);
            X509Certificate2Collection collection = store.Certificates.Find(X509FindType.FindBySubjectName, certificateSubject, checkValidity);
            if (collection != null && collection.Count > 0)
            {
                X509Certificate2 serverCertificate = collection[0];
                bool verifyCert = serverCertificate.Verify();
                LogManager.Debug("X509 certificate loaded from current user store, subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");
                return serverCertificate;
            }
            else
            {
                LogManager.Debug("X509 certificate with subject name=" + certificateSubject + ", not found in " + store.Location + " store.");
                return null;
            }
        }
    }
}
