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

            if (clientMachineName.ToLower() != "localhost")
            
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

            // Create an out of process PowerShell runspace and explictly use version 5.1
            // This is needed when running as a service, which is how the orchestrator extension operates
            // Interestingly this is not needd when running as a console application
            // TODO: Consider refactoring this so that we properly dispose of these objects instead of waiting on the GC

            PowerShellProcessInstance instance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
            Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), instance);

            return rs;
        }
    }
}
