using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class JobCertificateDTO
    {
        public string Thumbprint { get; set; }
        public string Contents { get; set; }
        public string Alias { get; set; }
        public string PrivateKeyPassword { get; set; }
    }
}
