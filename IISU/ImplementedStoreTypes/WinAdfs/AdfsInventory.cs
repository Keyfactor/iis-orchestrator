using System;
using Keyfactor.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinADFS
{
    public class AdfsFarmInventory
    {
        public string FarmName { get; set; }
        public string HostName { get; set; }
        public string Identifier { get; set; }
        public string ServiceAccountName { get; set; }
        public int FarmBehaviorLevel { get; set; }
        public List<AdfsNodeInfo> Nodes { get; set; } = new List<AdfsNodeInfo>();
        public List<AdfsCertificateInfo> Certificates { get; set; } = new List<AdfsCertificateInfo>();
        public DateTime InventoryDate { get; set; }
    }
}
