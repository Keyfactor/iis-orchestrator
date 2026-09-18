// Copyright 2026 Keyfactor
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

// Ignore Spelling: Keyfactor Ldap

using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinLdap
{
    // Used only by ReEnrollment.cs (via ClientPSCertStoreReEnrollment) to finish an ODKG job: the
    // certificate has already been imported into Cert:\LocalMachine\My by the shared
    // Import-KeyfactorSignedCertificate (Common) at that point - this registers it into the NTDS
    // service store, mirroring WinSqlBinding/WinIISBinding's role for their own store types.
    public class WinLdapBinding
    {
        private static ILogger _logger;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private static Collection<PSObject>? _results = null;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

        public static ResultObject RegisterCertificate(PSHelper psHelper, string thumbprint, string storePath)
        {
            _logger = LogHandler.GetClassLogger<WinLdapBinding>();
            _logger.MethodEntry();

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Thumbprint", thumbprint },
                    { "StoreName", storePath }
                };

                _results = psHelper.ExecutePowerShell("Register-KeyfactorLdapsCertificate", parameters);
                ResultObject result = ResultObject.FromPSResults(_results);
                _logger.LogTrace($"Register-KeyfactorLdapsCertificate returned Status={result.Status}, Code={result.Code}, Step={result.Step}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing PowerShell function: Register-KeyfactorLdapsCertificate");
                return new ResultObject
                {
                    Status = ResultObject.StatusError,
                    Code = -1,
                    Step = "CatchAll",
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
