using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinIIS
{
    public class IISCertificateInfo
    {
        public string SiteName { get; set; } //
        public string Binding { get; set; } //
        public string IPAddress { get; set; } //
        public string Port { get; set; } //
        public string Protocol { get; set; } //
        public string HostName { get; set; } //
        public string SNI { get; set; } //
        public string Certificate { get; set; } //
        public DateTime ExpiryDate { get; set; } //
        public string Issuer { get; set; } //
        public string Thumbprint { get; set; } //
        public bool HasPrivateKey { get; set; } //
        public string SAN { get; set; } //
        public string ProviderName { get; set; } //
        public string CertificateBase64 { get; set; } //
        public string FriendlyName { get; set; } //

    }
}
