using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.UnitTests
{
    public class AdfsUnitTests
    {

        [Fact]
        public void Test_AdfsInventory()
        {
            // Arrange
            RemoteSettings settings = new RemoteSettings
            {
                ClientMachineName = "{IPAddress}}",
                Protocol = "http",
                Port = "5985",
                IncludePortInSPN = true,
                ServerUserName = @"{username}",
                ServerPassword = "{password}"
            };

            // Act
            Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinAdfs.Inventory adfs = new();
            adfs.QueryWinADFSCertificates(settings, "My");

            // Assert

        }
    }
}
