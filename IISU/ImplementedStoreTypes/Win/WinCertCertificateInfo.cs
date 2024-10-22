using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.Win
{
    public class WinCertCertificateInfo
    {
        public string StoreName { get; set; }
        public string Certificate { get; set; }
        public string  ExpiryDate { get; set; }
        public string Issuer { get; set; }
        public string Thumbprint { get; set; }
        public bool HasPrivateKey { get; set; }
        public string SAN { get; set; }
        public string ProviderName { get; set; }
        public string Base64Data { get; set; }
    }
}
