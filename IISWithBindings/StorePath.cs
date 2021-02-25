using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    class StorePath
    {
        public string SiteName { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string HostName { get; set; }
        public string Protocol { get; set; }

        public StorePath (string siteName, string ipAddress, string port, string hostName)
        {
            SiteName = siteName;
            IP = ipAddress;
            Port = port;
            HostName = hostName;
            Protocol = "https";
        }

        public string FormatForIIS()
        {
            return $@"{IP}:{Port}:{HostName}";
        }
    }
}
