using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    class StorePath
    {
        [JsonProperty("siteName")]
        [DefaultValue("Default Web Site")]
        public string SiteName { get; set; }
        [JsonProperty("ipAddress")]
        public string IP { get; set; }
        [JsonProperty("port")]
        [DefaultValue("443")]
        public string Port { get; set; }
        [JsonProperty("hostName")]
        public string HostName { get; set; }
        [JsonProperty("protocol")]
        [DefaultValue("https")]
        public string Protocol { get; set; }
        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SPNPortFlag { get; set; }
        [JsonProperty("sniflag")]
        [DefaultValue(SniFlag.None)]
        public SniFlag SniFlag { get; set; }

        public StorePath()
        {

        }
        public StorePath (string siteName, string ipAddress, string port, string hostName)
        {
            SiteName = siteName;
            IP = ipAddress;
            Port = port;
            HostName = hostName;
            Protocol = "https";
        }

        public string FormatForIIS()
        {
            return $@"{IP}:{Port}:{HostName}";
        }
    }

    enum SniFlag
    {
        None = 0,
        SNI = 1,
        NoneCentral = 2,
        SniCentral = 3
    }
}
