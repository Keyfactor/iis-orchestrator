using System.ComponentModel;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    internal class StorePath
    {
        public StorePath()
        {
        }

        public StorePath(string siteName, string ipAddress, string port, string hostName)
        {
            SiteName = siteName;
            Ip = ipAddress;
            Port = port;
            HostName = hostName;
            Protocol = "https";
        }

        [JsonProperty("siteName")]
        [DefaultValue("Default Web Site")]
        public string SiteName { get; set; }

        [JsonProperty("ipAddress")] public string Ip { get; set; }

        [JsonProperty("port")]
        [DefaultValue("443")]
        public string Port { get; set; }

        [JsonProperty("hostName")] public string HostName { get; set; }

        [JsonProperty("protocol")]
        [DefaultValue("https")]
        public string Protocol { get; set; }

        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SpnPortFlag { get; set; }

        [JsonProperty("sniflag")]
        [DefaultValue(SniFlag.None)]
        public SniFlag SniFlag { get; set; }

        public string FormatForIIS()
        {
            return $@"{Ip}:{Port}:{HostName}";
        }
    }

    internal enum SniFlag
    {
        None = 0,
        Sni = 1,
        NoneCentral = 2,
        SniCentral = 3
    }
}