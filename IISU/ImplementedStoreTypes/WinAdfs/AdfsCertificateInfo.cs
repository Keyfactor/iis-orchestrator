using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinADFS
{
    public class AdfsCertificateInfo
    {
        public string CertificateType { get; set; }
        public bool IsPrimary { get; set; }
        public string Thumbprint { get; set; }
        public string Subject { get; set; }
        public string Issuer { get; set; }
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public int DaysUntilExpiry { get; set; }
        public bool IsExpired { get; set; }
        public bool IsExpiringSoon { get; set; }

        public override string ToString()
        {
            string status = IsExpired ? "EXPIRED" :
                           IsExpiringSoon ? $"Expires in {DaysUntilExpiry} days" :
                           "OK";
            return $"{CertificateType} ({(IsPrimary ? "Primary" : "Secondary")}): {status}";
        }
    }
}
