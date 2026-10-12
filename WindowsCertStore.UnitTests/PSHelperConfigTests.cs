using Keyfactor.Extensions.Orchestrator.WindowsCertStore;

namespace WindowsCertStore.UnitTests
{
    public class PSHelperConfigTests
    {
        [Fact]
        public void PSHelper_EmptyConstructor_DoesNotThrow()
        {
            var ex = Record.Exception(() => new PSHelper());
            Assert.Null(ex);
        }

        [Theory]
        [InlineData("")]
        [InlineData("keyfactor.wincert")]
        [InlineData("custom.jea.endpoint")]
        public void PSHelper_ParameterizedConstructor_DoesNotThrow(string jeaEndpoint)
        {
            var ex = Record.Exception(() =>
                new PSHelper("http", "5985", false, "localhost", "user", "pass", jeaEndpoint: jeaEndpoint));
            Assert.Null(ex);
        }

        [Theory]
        [InlineData("C:\\nonexistent\\path", "PowerShell", null)]
        public void FindScriptsDirectory_ReturnsNullForMissingDirectory(string baseDir, string folderName, string expected)
        {
            string result = PSHelper.FindScriptsDirectory(baseDir, folderName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FindScriptsDirectory_FindsPowerShellFolderFromBaseDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string result = PSHelper.FindScriptsDirectory(baseDir, "PowerShell");
            Assert.NotNull(result);
            Assert.True(Directory.Exists(result), $"Expected directory to exist: {result}");
        }

        [Theory]
        [InlineData("localhost")]
        [InlineData("LocalMachine")]
        [InlineData("localmachine")]
        public void PSHelper_Initialize_LocalMachineWithJEA_ThrowsAmbiguousConfigException(string localMachineName)
        {
            var ps = new PSHelper("http", "5985", false, localMachineName, "user", "pass", jeaEndpoint: "keyfactor.wincert");
            var ex = Record.Exception(() => ps.Initialize());
            Assert.NotNull(ex);
            Assert.Contains("Ambiguous configuration", ex.Message);
            Assert.Contains("keyfactor.wincert", ex.Message);
        }

        [Fact]
        public void PSHelper_Initialize_LocalMachineWithoutJEA_DoesNotThrowAmbiguousConfig()
        {
            // No JEA endpoint — local machine path should proceed past the guard clause
            // (it will fail later trying to create an out-of-process runspace in a test context,
            //  but the ambiguous-config exception specifically should NOT be thrown)
            var ps = new PSHelper("http", "5985", false, "localhost", "user", "pass", jeaEndpoint: "");
            var ex = Record.Exception(() => ps.Initialize());
            Assert.True(ex == null || !ex.Message.Contains("Ambiguous configuration"),
                $"Should not throw ambiguous config error, but got: {ex?.Message}");
        }
    }
}
