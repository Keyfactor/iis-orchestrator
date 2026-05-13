using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.IntegrationTests
{
    public class ClientConnection
    {
        public string? Machine { get; set; }
        public string StoreType { get; set; } = "";
        public string JEAEndpointName { get; set; } = "";
        public string? Username { get; set; }
        public string? PrivateKey { get; set; }
    }
}
