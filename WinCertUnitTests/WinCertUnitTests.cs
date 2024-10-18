using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            settings.ClientMachineName = "localMachine";
            settings.Protocol = "ssh";
            settings.Port = "443";
            settings.IncludePortInSPN = false;
            settings.ServerUserName = "administrator";
            settings.ServerPassword = "@dminP@ssword@";

            List<CurrentInventoryItem> certs = inv.QueryIISCertificates(settings);
        }
    }
}
