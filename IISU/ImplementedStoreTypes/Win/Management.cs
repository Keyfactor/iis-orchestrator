using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win
{
    internal class Management : IManagementJobExtension
    {
        public string ExtensionName => throw new NotImplementedException();

        public Management()
        {

        }

        public JobResult ProcessJob(ManagementJobConfiguration jobConfiguration)
        {
            WinIIS.IISManager mgr = new WinIIS.IISManager(jobConfiguration, "", "");

            throw new NotImplementedException();
        }
    }
}
