using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models
{
    internal class CertificateInfo
    {
        public string StoreName { get; set; }
        public string Certificate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Issuer { get; set; }
        public string Thumbprint { get; set; }
        public bool HasPrivateKey { get; set; }
        public string SAN { get; set; }
        public string ProviderName { get; set; }
        public string Base64Data { get; set; }
    }
}
