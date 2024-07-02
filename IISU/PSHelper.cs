// Copyright 2022 Keyfactor
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

using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class PsHelper
    {
        private static ILogger _logger;

        public static Runspace GetClientPsRunspace(string winRmProtocol, string clientMachineName, string winRmPort, bool includePortInSpn, string serverUserName, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<PsHelper>();
            _logger.MethodEntry();

            // 2.4 - Client Machine Name now follows the naming conventions of {clientMachineName}|{localMachine}
            // If the clientMachineName is just 'localhost', it will maintain that as locally only (as previously)
            // If there is no 2nd part to the clientMachineName, a remote PowerShell session will be created

            // Break the clientMachineName into parts
            string[] parts = clientMachineName.Split('|');

            // Extract the client machine name and arguments based upon the number of parts
            string machineName = parts.Length > 1 ? parts[0] : clientMachineName;
            string argument = parts.Length > 1 ? parts[1] : null;

            // Determine if this is truly a local connection
            bool isLocal = (machineName.ToLower() == "localhost") || (argument != null && argument.ToLower() == "localmachine");

            _logger.LogInformation($"Full clientMachineName={clientMachineName} | machineName={machineName} | argument={argument} | isLocal={isLocal}");

            if (isLocal)
            {
                //return RunspaceFactory.CreateRunspace();
                PowerShellProcessInstance instance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
                Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), instance);

                return rs;
            }
            else
            {
                var connInfo = new WSManConnectionInfo(new Uri($"{winRmProtocol}://{clientMachineName}:{winRmPort}/wsman"));
                connInfo.IncludePortInSPN = includePortInSpn;

                _logger.LogTrace($"Creating remote session at: {connInfo.ConnectionUri}");

                if (!string.IsNullOrEmpty(serverUserName))
                {
                    _logger.LogTrace($"Credentials Specified");
                    var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                    connInfo.Credential = new PSCredential(serverUserName, pw);
                }
                return RunspaceFactory.CreateRunspace(connInfo);
            }
        }

        public static IEnumerable<string> GetCSPList(Runspace myRunspace)
        {
            _logger.LogTrace("Getting the list of Crypto Service Providers");

            using var ps = PowerShell.Create();

            ps.Runspace = myRunspace;

            var certStoreScript = $@"
                                $certUtilOutput = certutil -csplist

                                $cspInfoList = @()
                                foreach ($line in $certUtilOutput) {{
                                    if ($line -match ""Provider Name:"") {{
                                        $cspName = ($line -split "":"")[1].Trim()
                                        $cspInfoList += $cspName
                                    }}
                                }}

                                $cspInfoList";

            ps.AddScript(certStoreScript);

            foreach (var result in ps.Invoke())
            {
                var cspName = result?.BaseObject?.ToString();
                if (cspName != null) { yield return cspName; }
            }

            _logger.LogInformation("No Crypto Service Providers were found");
            yield return null;
        }

        public static bool IsCSPFound(IEnumerable<string> cspList, string userCSP)
        {
            foreach (var csp in cspList)
            {
                if (string.Equals(csp, userCSP, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogTrace($"CSP found: {csp}");
                    return true;
                }
            }
            _logger.LogTrace($"CSP: {userCSP} was not found");
            return false;
        }
    }
}
