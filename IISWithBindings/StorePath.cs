using System.ComponentModel;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    internal class StorePath
    {

        public StorePath()
        {
        }

        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SpnPortFlag { get; set; }

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