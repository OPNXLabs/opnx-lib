using System.Text;

namespace OPNX.Lib.Common.Cryptography
{
    public static class SHA
    {
        public static string Sha256(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        public static string Sha512(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = System.Security.Cryptography.SHA512.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static string GetStringFromHash(byte[] hash)
        {
            StringBuilder result = new();
            for (int i = 0; i < hash.Length; i++)
            {
                result.Append(hash[i].ToString("X2"));
            }
            return result.ToString();
        }
    }
}
