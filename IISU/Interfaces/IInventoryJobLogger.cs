using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal interface IInventoryJobLogger : IJobConfigurationLoggerBase, IInventoryCertStoreDetails
    {
    }
}
