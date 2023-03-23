using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal interface IManagementCertStoreDetails
    {
        public CertificateStoreDetailsDTO CertificateStoreDetails { get; set; }
        public CertificateStoreDetailPropertiesDTO CertificateStoreDetailProperties { get; set; }
    }
}
