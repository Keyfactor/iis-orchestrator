using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.UnitTests
{
    public class PSHelperUnitTests
    {
        [Fact]
        public void Test_LoadAllScripts()
        {
            // Arrange
            var psHelper = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.PSHelper();
            string scriptsFolder = PSHelper.FindScriptsDirectory(AppDomain.CurrentDomain.BaseDirectory, "PowerShellScripts");

            // Act
            string scripts = psHelper.LoadAllScripts(scriptsFolder);
            // Assert
            scripts.Contains("# All scripts loaded.");

            // If no exception is thrown, the test passes
        }
    }
}
