using System;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    public class PsCertificate
    {
        public string Thumbprint { get; set; }
        public byte[] RawData { get; set; }
        public bool HasPrivateKey { get; set; }
        public string CertificateData => Convert.ToBase64String(RawData);
    }
}