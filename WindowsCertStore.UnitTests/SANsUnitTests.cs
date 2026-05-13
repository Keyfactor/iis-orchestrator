using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using Keyfactor.Orchestrators.Extensions;

namespace WindowsCertStore.UnitTests
{
    public class SANsUnitTests
    {

        private ClientPSCertStoreReEnrollment enrollment = new ClientPSCertStoreReEnrollment();

        [Fact]
        public void Test_SANs()
        {
            // Arrange
            var sans = new Dictionary<string, string[]>
            {
                { "dns", new[] { "example.com", "www.example.com" } },
                { "ip", new[] { "192.168.1.1", "2001:0db8:85a3:0000:0000:8a2e:0370:7334" } },
                { "email", new[] { "myemail@company.com" } },
                { "uri", new[] { "http://mycompany.com" } },
                { "upn", new[] { "myusername@company.com" } }
                };

            // Act
            var sanBuilder = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.SANBuilder(sans);
            string sanString = sanBuilder.BuildSanString();
            string sanToString = sanBuilder.ToString();

            // Assert
            Assert.Equal("dns=example.com&dns=www.example.com&ipaddress=192.168.1.1&ipaddress=2001:0db8:85a3:0000:0000:8a2e:0370:7334&email=myemail@company.com&uri=http://mycompany.com&upn=myusername@company.com", sanString);
            Assert.Contains("DNS: example.com, www.example.com", sanToString);
        }
        [Fact]
        public void ResolveSanString_PrefersConfigSANs_WhenBothSourcesExist()
        {
            // Arrange
            var config = new ReenrollmentJobConfiguration
            {
                JobProperties = new Dictionary<string, object>
                {
                    { "SAN", "dns=legacy.example.com&dns=old.example.com" }
                },
                SANs = new Dictionary<string, string[]>
                {
                    { "dns", new[] { "example.com", "www.example.com" } },
                    { "ip", new[] { "192.168.1.1" } },
                    { "email", new[] { "user@mycompany.com" } }
                }
            };

            // Act
            string result =  enrollment.ResolveSANString(config);

            // Assert
            Assert.Contains("dns=example.com", result);
            Assert.Contains("dns=www.example.com", result);
            Assert.Contains("ipaddress=192.168.1.1", result);
            Assert.Contains("email=user@mycompany.com", result);
            Assert.DoesNotContain("legacy.example.com", result); // ensure legacy ignored
        }

        [Theory]
        [InlineData("dns=legacy.example.com&dns=old.example.com")]
        [InlineData("")]
        [InlineData(null)]
        public void ResolveSanString_UsesLegacySAN_WhenConfigSANsMissing(string legacySan)
        {
            // Arrange
            var config = new ReenrollmentJobConfiguration
            {
                JobProperties = new Dictionary<string, object>
                {
                    { "SAN", legacySan }
                },
                SANs = new Dictionary<string, string[]>()
            };

            // Act
            string result = enrollment.ResolveSANString(config);

            // Assert
            //Assert.Equal(legacySan, result);

            // Assert
            if (legacySan == null)
            {
                Assert.Equal(string.Empty, result);
            }
            else if (legacySan == string.Empty)
            {
                Assert.Equal(string.Empty, result);
            }
            else
            {
                Assert.Equal(legacySan, result);
            }
        }

        [Fact]
        public void ResolveSanString_ReturnsEmpty_WhenNoSANsProvided()
        {
            // Arrange
            var config = new ReenrollmentJobConfiguration();

            // Act
            string result = enrollment.ResolveSANString(config);

            // Assert
            Assert.Equal(string.Empty, result);
        }
    }
}