using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace WindowsCertStore.UnitTests
{
    public class JobPropertiesTests
    {
        private static JobProperties Deserialize(string json) =>
            JsonConvert.DeserializeObject<JobProperties>(json,
                new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate })
            ?? new JobProperties();

        [Fact]
        public void JobProperties_JEAEndpointName_DefaultsToEmptyString()
        {
            var props = Deserialize("{}");
            Assert.Equal("", props.JEAEndpointName);
        }

        [Fact]
        public void JobProperties_JEAEndpointName_ReadsValue()
        {
            var props = Deserialize("{\"JEAEndpointName\": \"keyfactor.wincert\"}");
            Assert.Equal("keyfactor.wincert", props.JEAEndpointName);
        }

        [Fact]
        public void JobProperties_UseJEA_LegacyField_IsIgnored()
        {
            // JSON with a UseJEA field that no longer exists should deserialize without error
            var props = Deserialize("{\"UseJEA\": true, \"JEAEndpointName\": \"\"}");
            Assert.NotNull(props);
            Assert.Equal("", props.JEAEndpointName);
        }

        [Fact]
        public void JobProperties_WinRmProtocol_DefaultsToHttp()
        {
            var props = Deserialize("{}");
            Assert.Equal("http", props.WinRmProtocol);
        }

        [Fact]
        public void JobProperties_WinRmPort_DefaultsTo5985()
        {
            var props = Deserialize("{}");
            Assert.Equal("5985", props.WinRmPort);
        }

        [Fact]
        public void JobProperties_SpnPortFlag_DefaultsToFalse()
        {
            var props = Deserialize("{}");
            Assert.False(props.SpnPortFlag);
        }

        [Fact]
        public void JobProperties_FullJson_ParsesCorrectly()
        {
            string json = JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                ["spnwithport"] = "false",
                ["WinRm Protocol"] = "https",
                ["WinRm Port"] = "5986",
                ["JEAEndpointName"] = "my.jea.endpoint"
            });

            var props = Deserialize(json);
            Assert.False(props.SpnPortFlag);
            Assert.Equal("https", props.WinRmProtocol);
            Assert.Equal("5986", props.WinRmPort);
            Assert.Equal("my.jea.endpoint", props.JEAEndpointName);
        }
    }
}
