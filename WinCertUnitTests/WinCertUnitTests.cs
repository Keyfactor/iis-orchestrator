using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert;
using Keyfactor.Orchestrators.Extensions;

namespace WinCertUnitTests
{
    [TestClass]
    public class WinCertUnitTests
    {
        [TestMethod]
        public void TestGetInventory()
        {
            Inventory inv = new();
            RemoteSettings settings = new();
            settings.ClientMachineName = "vmlabsvr1";
            settings.Protocol = "http";
            settings.Port = "5985";
            settings.IncludePortInSPN = false;
            settings.ServerUserName = "administrator";
            settings.ServerPassword = "@dminP@ssword%";

            // This function calls the Get-KFCertificates function and take the StoreName argument
            List<CurrentInventoryItem> certs = inv.QueryWinCertCertificates(settings, "My");
        }

        [TestMethod]
        public void TestAddCertificateToStore()
        {

        }
    }
}
