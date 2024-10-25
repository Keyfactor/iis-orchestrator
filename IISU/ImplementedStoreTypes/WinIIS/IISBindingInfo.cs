using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinIIS
{
    public class IISBindingInfo
    {
        public string SiteName { get; set; }
        public string Protocol { get; set; }
        public string IPAddress { get; set; }
        public string Port { get; set; }
        public string? HostName { get; set; }
        public string SniFlag { get; set; }

        public IISBindingInfo(Dictionary<string, object> bindingInfo)
        {
            SiteName = bindingInfo["SiteName"].ToString();
            Protocol = bindingInfo["Protocol"].ToString();
            IPAddress = bindingInfo["IPAddress"].ToString();
            Port = bindingInfo["Port"].ToString();
            HostName = bindingInfo["HostName"]?.ToString();
            SniFlag = MigrateSNIFlag(bindingInfo["SniFlag"].ToString());
        }

        private string MigrateSNIFlag(string input)
        {
            // Check if the input is numeric, if so, just return it as an integer
            if (int.TryParse(input, out int numericValue))
            {
                return numericValue.ToString();
            }

            if (string.IsNullOrEmpty(input)) { throw new ArgumentNullException("SNI/SSL Flag", "The SNI or SSL Flag flag must not be empty or null."); }

            // Handle the string cases
            switch (input.ToLower())
            {
                case "0 - no sni":
                    return "0";
                case "1 - sni enabled":
                    return "1";
                case "2 - non sni binding":
                    return "2";
                case "3 - sni binding":
                    return "3";
                default:
                    throw new ArgumentOutOfRangeException($"Received an invalid value '{input}' for sni/ssl Flag value");
            }
        }
    }
}
