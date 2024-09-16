using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using System.Security.Cryptography.X509Certificates;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
using System.Net;
//using System.Web.Services.Description;
using System.Management.Automation;
using Keyfactor.Orchestrators.Common.Enums;
using System.Security.Policy;
using Microsoft.Web.Administration;
namespace WinCertUnitTests
{
    [TestClass]
    public class UnitTestIISBinding
    {
        private string certName = "";
        private string certPassword = "";
        private string pfxPath = "";

        public UnitTestIISBinding() 
        {
            certName = "UnitTestCertificate";
            certPassword = "lkjglj655asd";
            pfxPath = Path.Combine(Directory.GetCurrentDirectory(), "TestCertificate.pfx");

            if (!File.Exists(pfxPath))
            {
                CertificateHelper.CreateSelfSignedCertificate(certName, certPassword, pfxPath);
            }
        }


        [TestMethod]
        public void RenewBindingCertificate()
        {
            string certPath = @"Assets\ManualCert_8zWwF36N6cNu.pfx";
            string password = "8zWwF36N6cNu";
            X509Certificate2 cert = new X509Certificate2(pfxPath, certPassword);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");

            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", cert.Thumbprint, "My", "0");
            JobResult result = IIS.BindCertificate(cert);
            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void UnBindCertificate()
        {
            X509Certificate2 cert = new X509Certificate2(pfxPath, certPassword);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");
            BindingNewCertificate();

            string sslFlag = "0";
            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", "", "My", sslFlag);
            JobResult result = IIS.UnBindCertificate();

            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void BindingNewCertificate()
        {
            string certPath = @"Assets\ManualCert_8zWwF36N6cNu.pfx";
            string password = "8zWwF36N6cNu";
            X509Certificate2 cert = new X509Certificate2(pfxPath, certPassword);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");

            string sslFlag = "0";

            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", "", "My", sslFlag);
            JobResult result = IIS.BindCertificate(cert);

            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void BindingNewCertificateBadSslFlag()
        {
            X509Certificate2 cert = new X509Certificate2(pfxPath, certPassword);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");

            string sslFlag = "909"; // known bad value

            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", "", "My", sslFlag);
            JobResult result = IIS.BindCertificate(cert);

            Assert.AreEqual("Failure", result.Result.ToString());
        }

        [TestMethod]
        public void AddCertificate()
        {
            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");
            rs.Open();

            ClientPSCertStoreManager certStoreManager = new ClientPSCertStoreManager(rs);
            JobResult result = certStoreManager.ImportPFXFile(pfxPath, certPassword, "", "My");
            rs.Close();

            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void RemoveCertificate()
        {
            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");
            rs.Open();

            ClientPSCertStoreManager certStoreManager = new ClientPSCertStoreManager(rs);
            try
            {
                certStoreManager.RemoveCertificate("0a3f880aa17c03ef2c75493497d89756cfafa165", "My");
                Assert.IsTrue(true, "Certificate was successfully removed.");
            }
            catch (Exception e)
            {
                Assert.IsFalse(false, e.Message);
            }
            rs.Close();
        }


        [TestMethod]
        public void GetBoundCertificates()
        {
            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");
            rs.Open();
            WinIISInventory IISInventory = new WinIISInventory();
            List<CurrentInventoryItem> certs =  IISInventory.GetInventoryItems(rs, "My");
            rs.Close();

            Assert.IsNotNull(certs);

        }

        [TestMethod]
        public void OrigSNIFlagZeroReturnsZero()
        {
            string expectedResult = "32";
            string result = ClientPSIIManager.MigrateSNIFlag("32");
            Assert.AreEqual(expectedResult, result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void InvalidSNIFlagThrowException()
        {
            string result = ClientPSIIManager.MigrateSNIFlag("Bad value");
        }

        static bool TestValidSslFlag(int sslFlag)
        {
            try
            {
                using (ServerManager serverManager = new ServerManager())
                {
                    // Loop through all sites in IIS
                    foreach (Microsoft.Web.Administration.Site site in serverManager.Sites)
                    {
                        // Loop through all bindings for each site
                        foreach (Binding binding in site.Bindings)
                        {
                            // Check if the binding uses the HTTPS protocol
                            if (binding.Protocol == "https")
                            {
                                // Get the SslFlags value (stored in binding.Attributes)
                                int currentSslFlags = (int)binding.Attributes["sslFlags"].Value;

                                // Check if the SslFlag value matches the provided one
                                if (currentSslFlags == sslFlag)
                                {
                                    return true;  // Valid SslFlag found
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            return false;  // No matching SslFlag found
        }
    }
}