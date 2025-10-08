using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.IntegrationTests.Factories
{
    public static class CertificateFactory
    {
        /// <summary>
        /// Creates a self-signed certificate, exports it as a PFX with a password,
        /// and returns the thumbprint and base64-encoded PFX.
        /// </summary>
        /// <param name="subjectName">The subject name for the certificate (CN=...)</param>
        /// <param name="pfxPassword">The password to protect the PFX file</param>
        /// <returns>Tuple of Thumbprint and Base64 PFX string</returns>
        public static (string Thumbprint, string Base64Pfx) CreateSelfSignedCert(string subjectName, string pfxPassword)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var distinguishedName = new X500DistinguishedName($"CN={subjectName}");

                var request = new CertificateRequest(
                    distinguishedName,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                // Add key usage & basic constraints (minimal for test certs)
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                // Valid for 1 year
                DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                DateTimeOffset notAfter = notBefore.AddYears(1);

                using (X509Certificate2 cert = request.CreateSelfSigned(notBefore, notAfter))
                {
                    // Export with private key to PFX
                    byte[] pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);

                    // Convert PFX to base64
                    string base64Pfx = Convert.ToBase64String(pfxBytes);

                    // Thumbprint (uppercase, no spaces)
                    string thumbprint = cert.Thumbprint?.Replace(" ", "").ToUpperInvariant();

                    return (thumbprint, base64Pfx);
                }
            }
        }

        /// <summary>
        /// Generates a random PFX password.
        /// </summary>
        /// <param name="length">Length of the password (default 24)</param>
        /// <returns>Randomly generated password string</returns>
        public static string GeneratePfxPassword(int length = 24)
        {
            if (length < 8)
                throw new ArgumentException("Password length must be at least 8 characters.");

            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()-_=+[]{}<>?";

            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(length);
            foreach (var b in bytes)
            {
                sb.Append(validChars[b % validChars.Length]);
            }

            return sb.ToString();
        }
    }
}
