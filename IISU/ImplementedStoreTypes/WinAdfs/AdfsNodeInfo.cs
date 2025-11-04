using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinADFS
{
    public class AdfsNodeInfo
    {
        public string NodeName { get; set; }
        public string Role { get; set; }
        public bool IsReachable { get; set; }
        public string ServiceStatus { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string SyncStatus { get; set; }
        public string ErrorMessage { get; set; }
    }
}
