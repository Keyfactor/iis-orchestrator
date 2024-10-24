using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class RemoteSettings
    {
        public string ClientMachineName { get; set; }
        public string Protocol{ get; set; }
        public string Port { get; set; }
        public bool IncludePortInSPN { get; set; }

        public string ServerUserName { get; set; }
        public string ServerPassword { get; set; }

    }

}
