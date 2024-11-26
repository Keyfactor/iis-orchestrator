// Ignore Spelling: Keyfactor sql

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql
{

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
