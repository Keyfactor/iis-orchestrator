using System.ComponentModel;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    internal class StorePath
    {

        public StorePath()
        {
        }

        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SpnPortFlag { get; set; }

        [JsonProperty("WinRm Protocol")]
        [DefaultValue("http")]
        public string WinRmProtocol { get; set; }

        [JsonProperty("WinRm Port")]
        [DefaultValue("5985")]
        public string WinRmPort { get; set; }

        [JsonProperty("sniflag")]
        [DefaultValue(SniFlag.None)]
        public SniFlag SniFlag { get; set; }

    }

    internal enum SniFlag
    {
        None = 0,
        Sni = 1,
        NoneCentral = 2,
        SniCentral = 3
    }
}