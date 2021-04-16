using System;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    public class PSCertificate
    {
        public string Thumbprint { get; set; }
        public byte[] RawData { get; set; }
        public bool HasPrivateKey { get; set; }
        public string CertificateData { get => Convert.ToBase64String(this.RawData); }
    }
}