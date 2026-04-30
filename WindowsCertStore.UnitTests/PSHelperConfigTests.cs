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
        [InlineData(null, "testdir", null)]
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
    }
}
