using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class CertificateStoreDetailsDTO
    {
        public string ClientMachine { get; set; }
        public string StorePath { get; set; }
        public string StorePassword { get; set; }
        public int Type { get; set; }
    }
}
