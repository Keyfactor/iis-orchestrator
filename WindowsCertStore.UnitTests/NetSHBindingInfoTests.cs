using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinNetSH;

namespace WindowsCertStore.UnitTests
{
    public class NetSHBindingInfoTests
    {
        [Fact]
        public void ParseAliasBindingString_IPPortOnly_ParsesCorrectly()
        {
            var info = NetSHBindingInfo.ParseAliasBindingString("ABCDEF1234567890:0.0.0.0:443");

            Assert.Equal("ABCDEF1234567890", info.Thumbprint);
            Assert.Equal("0.0.0.0", info.IPAddress);
            Assert.Equal("443", info.Port);
            Assert.Null(info.HostName);
        }

        [Fact]
        public void ParseAliasBindingString_WithHostName_ParsesCorrectly()
        {
            var info = NetSHBindingInfo.ParseAliasBindingString("ABCDEF1234567890:0.0.0.0:443:www.example.com");

            Assert.Equal("ABCDEF1234567890", info.Thumbprint);
            Assert.Equal("0.0.0.0", info.IPAddress);
            Assert.Equal("443", info.Port);
            Assert.Equal("www.example.com", info.HostName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseAliasBindingString_NullOrWhitespace_ThrowsArgumentException(string alias)
        {
            Assert.Throws<ArgumentException>(() => NetSHBindingInfo.ParseAliasBindingString(alias));
        }

        [Theory]
        [InlineData("ThumbprintOnly")]
        [InlineData("Thumbprint:IPAddress")]
        [InlineData("Thumbprint:IPAddress:Port:HostName:ExtraPart")]
        public void ParseAliasBindingString_WrongPartCount_ThrowsFormatException(string alias)
        {
            Assert.Throws<FormatException>(() => NetSHBindingInfo.ParseAliasBindingString(alias));
        }

        [Fact]
        public void Constructor_FromDictionary_WithAllKeys_PopulatesFields()
        {
            var dict = new Dictionary<string, object>
            {
                { "IPAddress", "192.168.1.1" },
                { "Port", "8443" },
                { "HostName", "www.example.com" },
                { "AppId", "{11111111-2222-3333-4444-555555555555}" }
            };

            var info = new NetSHBindingInfo(dict);

            Assert.Equal("192.168.1.1", info.IPAddress);
            Assert.Equal("8443", info.Port);
            Assert.Equal("www.example.com", info.HostName);
            Assert.Equal("{11111111-2222-3333-4444-555555555555}", info.AppId);
        }

        [Fact]
        public void Constructor_FromDictionary_MissingOptionalKeys_LeavesThemNull()
        {
            var dict = new Dictionary<string, object>
            {
                { "IPAddress", "192.168.1.1" },
                { "Port", "443" }
            };

            var info = new NetSHBindingInfo(dict);

            Assert.Null(info.HostName);
            Assert.Null(info.AppId);
        }

        [Fact]
        public void Constructor_FromDictionary_MissingIPAddress_ThrowsArgumentException()
        {
            var dict = new Dictionary<string, object>
            {
                { "Port", "443" }
            };

            Assert.Throws<ArgumentException>(() => new NetSHBindingInfo(dict));
        }

        [Fact]
        public void Constructor_FromDictionary_MissingPort_ThrowsArgumentException()
        {
            var dict = new Dictionary<string, object>
            {
                { "IPAddress", "192.168.1.1" }
            };

            Assert.Throws<ArgumentException>(() => new NetSHBindingInfo(dict));
        }
    }
}
