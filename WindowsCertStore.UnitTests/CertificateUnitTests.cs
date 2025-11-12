using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.UnitTests
{
    public class CertificateUnitTests
    {
        [Fact]
        public void Test_GetCertificateTempPFX_WithValidBase64String_ReturnsFilePath()
        {
            // Arrange
            string base64Cert = "VGhpcyBpcyBzb21lIGJhc2UgNjQgc3RyaW5nIGluZm9ybWF0aW9u";

            // Act
            string tempFilePath = Keyfactor.Extensions.Orchestrator.WindowsCertStore.Certificate.Utilities.WriteCertificateToTempPfx(base64Cert);

            // Assert
            Assert.False(string.IsNullOrEmpty(tempFilePath));
            Assert.True(System.IO.File.Exists(tempFilePath));

        }
    }
}
