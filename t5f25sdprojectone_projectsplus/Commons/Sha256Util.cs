using System;
using System.Security.Cryptography;
using System.Text;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public static class Sha256Util
    {
        public static string ToHexSha256(string input)
        {
            if (input == null) input = string.Empty;
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
