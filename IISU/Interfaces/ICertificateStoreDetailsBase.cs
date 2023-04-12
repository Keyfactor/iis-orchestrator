using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal interface ICertificateStoreDetailsBase
    {
        public CertificateStoreDetailsDTO CertificateStoreDetails { get; set; }
    }
}
