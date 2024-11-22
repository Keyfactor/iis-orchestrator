using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinSQL
{
    //public  class WinSQLCertificateInfo
    //{
    //    public string InstanceName { get; set; }
    //    public string StoreName { get; set; }
    //    public string Certificate { get; set; }
    //    public string ExpiryDate { get; set; }
    //    public string Issuer { get; set; }
    //    public string Thumbprint { get; set; }
    //    public bool HasPrivateKey { get; set; }
    //    public string SAN { get; set; }
    //    public string ProviderName { get; set; }
    //    public string Base64Data { get; set; }
    //}

    public class Parameters
    {
        public string InstanceName { get; set; }
        public object ProviderName { get; set; }
    }

    public class WinSQLCertificateInfo
    {
        public string Certificates { get; set; }
        public string Alias { get; set; }
        public bool PrivateKeyEntry { get; set; }
        public bool UseChainLevel { get; set; }
        public string ItemStatus { get; set; }
        public Parameters Parameters { get; set; }
    }
}
