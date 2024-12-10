using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WinCertUnitTests
{
    internal class CertificateHelper
    {
        public static void CreateSelfSignedCertificate(string certName, string password, string pfxPath)
        {
            // Set certificate subject and other properties
            var distinguishedName = new X500DistinguishedName($"CN={certName}");

            using (RSA rsa = RSA.Create(2048))
            {
                // Define the certificate request
                var certificateRequest = new CertificateRequest(
                    distinguishedName,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                // Add key usage and enhanced key usage (EKU) extensions
                certificateRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

                certificateRequest.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection
                        {
                        new Oid("1.3.6.1.5.5.7.3.1") // OID for Server Authentication
                        }, true));

                // Create the self-signed certificate
                var startDate = DateTimeOffset.Now;
                var endDate = startDate.AddYears(1);

                using (X509Certificate2 certificate = certificateRequest.CreateSelfSigned(startDate, endDate))
                {
                    // Export the certificate with a password to a PFX file
                    byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, password);

                    // Save to a file
                    File.WriteAllBytes(pfxPath, pfxBytes);

                    Console.WriteLine($"Certificate created and saved at {pfxPath}");
                }
            }
        }
    }
}
