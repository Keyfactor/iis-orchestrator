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

using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class PSHelper
    {
        private static ILogger _logger;

        public static Runspace GetClientPSRunspace(string winRmProtocol, string clientMachineName, string WinRmPort, bool includePortInSPN, string serverUserName, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<PSHelper>();
            _logger.MethodEntry();
            if (clientMachineName.ToLower() != "localhost")
            {
                var connInfo = new WSManConnectionInfo(new Uri($"{winRmProtocol}://{clientMachineName}:{WinRmPort}/wsman"));
            connInfo.IncludePortInSPN = includePortInSPN;
            if (!string.IsNullOrEmpty(serverUserName))
            {
                _logger.LogTrace($"Credentials Specified");
                var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                connInfo.Credential = new PSCredential(serverUserName, pw);
            }
            return RunspaceFactory.CreateRunspace(connInfo);
            }
            else return RunspaceFactory.CreateRunspace();
        }
    }
}
