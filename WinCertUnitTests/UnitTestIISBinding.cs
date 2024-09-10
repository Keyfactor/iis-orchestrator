using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using System.Security.Cryptography.X509Certificates;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
using System.Net;
using System.Web.Services.Description;
using System.Management.Automation;
using Keyfactor.Orchestrators.Common.Enums;
namespace WinCertUnitTests
{
    [TestClass]
    public class UnitTestIISBinding
    {
        [TestMethod]
        public void RenewBindingCertificate()
        {
            string certPath = @"Assets\ManualCert_8zWwF36N6cNu.pfx";
            string password = "8zWwF36N6cNu";
            X509Certificate2 cert = new X509Certificate2(certPath, password);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");

            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", cert.Thumbprint, "My", "0");
            JobResult result = IIS.BindCertificate(cert);
            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void BindingNewCertificate()
        {
            string certPath = @"Assets\ManualCert_8zWwF36N6cNu.pfx";
            string password = "8zWwF36N6cNu";
            X509Certificate2 cert = new X509Certificate2(certPath, password);

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");

            ClientPSIIManager IIS = new ClientPSIIManager(rs, "Default Web Site", "https", "*", "443", "", "", "My", "32");
            JobResult result = IIS.BindCertificate(cert);

            Assert.AreEqual("Success", result.Result.ToString());
        }

        [TestMethod]
        public void AddCertificate()
        {

            string certPath = @"Assets\ManualCert_8zWwF36N6cNu.pfx";
            string password = "8zWwF36N6cNu";

            Runspace rs = PsHelper.GetClientPsRunspace("", "localhost", "", false, "", "");
            rs.Open();

            ClientPSCertStoreManager certStoreManager = new ClientPSCertStoreManager(rs);
            JobResult result = certStoreManager.ImportPFXFile(certPath, password, "", "My");
            rs.Close();

            Assert.AreEqual("Success", result.Result.ToString());
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

    }
}