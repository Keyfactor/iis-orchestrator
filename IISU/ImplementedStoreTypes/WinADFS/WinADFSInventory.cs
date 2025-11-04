using System;
using Keyfactor.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinADFS
{
    internal class WinADFSInventory
    {
        public static object GetCertificates(PSHelper psHelper, string storePath, ILogger logger)
        {
            throw new NotImplementedException();
        }
    }
}
