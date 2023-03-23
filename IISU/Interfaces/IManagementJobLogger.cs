using Keyfactor.Orchestrators.Common.Enums;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal interface IManagementJobLogger : IJobConfigurationLoggerBase, IManagementCertStoreDetails
    {
        public CertStoreOperationType OperationType { get; set; }
        public bool Overwrite { get; set; }

        public JobCertificateDTO JobCertificateProperties { get; set; }

    }
}
