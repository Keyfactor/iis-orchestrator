using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class CertificateStoreDetailPropertiesDTO
    {
        public string SiteName { get; set; }
        public string Port { get; set; }
        public string HostName { get; set; }
        public string Protocol { get; set; }
        public string SniFlag { get; set; }
        public string IPAddress { get; set; }
        public string ProviderName { get; set; }
        public string SAN { get; set; }
    }
}
