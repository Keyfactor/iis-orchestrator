using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal interface IJobConfigurationLoggerBase
    {
        public bool JobCancelled { get; set; }
        public ServerFault ServerError { get; set; }
        public long JobHistoryID { get; set; }
        public int RequestStatus { get; set; }
        public string ServerUserName { get; set; }
        public string ServerPassword { get; set; }
        public JobProperties JobConfigurationProperties { get; set; }
        public bool UseSSL { get; set; }
        public Guid JobTypeID { get; set; }
        public Guid JobID { get; set; }
        public string Capability { get; set; }

        public IEnumerable<PreviousInventoryItem> LastInventory { get; set; }
    }
}
