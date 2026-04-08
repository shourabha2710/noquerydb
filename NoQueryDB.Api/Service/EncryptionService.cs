using System.Security.Cryptography;
using System.Text;

namespace NoQueryDB.Api.Service
{
    public interface ISecretProtector
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
    public class EncryptionService : ISecretProtector
    {
        private readonly byte[] _key;

        public EncryptionService(IConfiguration config)
        {
            // 🔐 32 chars minimum
            var secret = config["EncryptionKey"];
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                throw new Exception("EncryptionKey must be at least 32 characters");

            _key = Encoding.UTF8.GetBytes(secret[..32]);
        }

        public string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            var encryptor = aes.CreateEncryptor();
            var inputBytes = Encoding.UTF8.GetBytes(plainText);
            var cipher = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);

            return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
        }

        public string Decrypt(string cipherText)
        {
            var full = Convert.FromBase64String(cipherText);
            var iv = full.Take(16).ToArray();
            var cipher = full.Skip(16).ToArray();

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;

            var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            return Encoding.UTF8.GetString(plain);
        }
    }
}
