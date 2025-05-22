// Copyright 2025 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Ignore Spelling: Keyfactor IISU

// 021225 rcp   2.6.0   Cleaned up and verified code

using System;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
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
            if (int.TryParse(input, out int numericValue))
            {
                return numericValue.ToString();
            }

            if (string.IsNullOrEmpty(input))
                throw new ArgumentNullException("SNI/SSL Flag", "The SNI or SSL Flag must not be empty or null.");

            // Normalize input
            var trimmedInput = input.Trim().ToLowerInvariant();

            // Handle boolean values
            if (trimmedInput == "true")
                return "1";
            if (trimmedInput == "false")
                return "0";

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
