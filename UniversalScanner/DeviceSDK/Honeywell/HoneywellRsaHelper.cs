using System;
using System.Security.Cryptography;
using System.Text;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Minimal RSA helper for the Honeywell HC30/HC35 activation protocol.
    /// The camera serves a PEM-encoded RSA-2048 public key via getState; the client
    /// must encrypt a small payload with PKCS#1 v1.5 padding and send back base64.
    /// .NET 4.8 has no ImportFromPem, so the SubjectPublicKeyInfo DER is parsed
    /// manually — for RSA-2048 the structure is fixed and trivial to decode.
    /// </summary>
    internal static class HoneywellRsaHelper
    {
        /// <summary>
        /// Encrypts <paramref name="plaintext"/> with the given PEM RSA public key
        /// using PKCS#1 v1.5 padding and returns base64 of the cipher block.
        /// </summary>
        public static string EncryptPkcs1Base64(string pemPublicKey, string plaintext)
        {
            RSAParameters rsaParams = ParseRsaPublicKeyPem(pemPublicKey);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(rsaParams);
                byte[] cipher = rsa.Encrypt(Encoding.UTF8.GetBytes(plaintext), false);
                return Convert.ToBase64String(cipher);
            }
        }

        /// <summary>
        /// Generates a 32-character cnonce in the exact form used by the camera's
        /// web UI: 32 random picks from the alphabet "abcdef0123456789-".
        /// (Reverse-engineered verbatim from the camera's JS bundle.)
        /// </summary>
        public static string GenerateCnonceRaw()
        {
            const string alphabet = "abcdef0123456789-";
            byte[] random = new byte[32];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);

            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < 32; i++)
                sb.Append(alphabet[random[i] % alphabet.Length]);
            return sb.ToString();
        }

        /// <summary>
        /// SHA-256 of the input as lowercase hex (64 chars), matching CryptoJS's
        /// default WordArray.toString() output.
        /// </summary>
        public static string Sha256Hex(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// AES-256-CBC encrypt with PKCS#7 padding. Key and IV are treated as raw
        /// bytes (the camera's JS passes them via CryptoJS.enc.Utf8.parse, which
        /// is byte-equivalent to ASCII bytes when the source string is hex/ASCII).
        /// Returns the ciphertext base64-encoded (matches CryptoJS's CipherParams
        /// .toString() when called with an explicit key WordArray).
        /// </summary>
        public static string AesCbcEncryptBase64(byte[] key, byte[] iv, string plaintext)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                byte[] data = Encoding.UTF8.GetBytes(plaintext);
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] cipher = enc.TransformFinalBlock(data, 0, data.Length);
                    return Convert.ToBase64String(cipher);
                }
            }
        }

        /// <summary>
        /// Parses a PEM-encoded RSA public key (X.509 SubjectPublicKeyInfo wrapper)
        /// into RSAParameters. Tolerant of CR/LF and stray whitespace.
        /// </summary>
        private static RSAParameters ParseRsaPublicKeyPem(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem))
                throw new ArgumentException("PEM is empty", "pem");

            string body = pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("\\n", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace(" ", "")
                .Trim();

            byte[] der = Convert.FromBase64String(body);

            // The DER is a SubjectPublicKeyInfo. Inside the BIT STRING is a SEQUENCE
            // containing two INTEGERs: modulus then publicExponent.
            // For RSA-2048 the modulus is 256 bytes (sometimes prefixed by a 0x00
            // sign byte, making the INTEGER 257 bytes long).
            // We scan for the first INTEGER with a 2-byte length descriptor whose
            // length is 256 or 257 — that's the modulus.

            int i = 0;
            while (i < der.Length - 5)
            {
                if (der[i] == 0x02 && der[i + 1] == 0x82)
                {
                    int len = (der[i + 2] << 8) | der[i + 3];
                    if (len == 256 || len == 257)
                    {
                        int mStart = i + 4;
                        int mLen = len;
                        if (der[mStart] == 0x00) { mStart++; mLen--; }

                        byte[] modulus = new byte[mLen];
                        Array.Copy(der, mStart, modulus, 0, mLen);

                        // Expect an INTEGER immediately after for the exponent.
                        int eTagPos = mStart + mLen;
                        if (eTagPos >= der.Length - 2 || der[eTagPos] != 0x02)
                            throw new InvalidOperationException("Expected exponent INTEGER tag after modulus");

                        int eLen = der[eTagPos + 1];
                        byte[] exponent = new byte[eLen];
                        Array.Copy(der, eTagPos + 2, exponent, 0, eLen);

                        return new RSAParameters
                        {
                            Modulus  = modulus,
                            Exponent = exponent,
                        };
                    }
                }
                i++;
            }

            throw new InvalidOperationException("RSA modulus not found in DER stream");
        }
    }
}
