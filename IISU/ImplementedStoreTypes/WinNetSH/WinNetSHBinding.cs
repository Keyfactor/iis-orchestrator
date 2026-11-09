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

// Ignore Spelling: Keyfactor NetSH Sslcert

using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinNetSH
{
    public class WinNetSHBinding
    {
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private static ILogger? _logger;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

        // Returns the full ResultObject (rather than the bool WinIISBinding.UnBindCertificate uses)
        // because callers need to read the resolved AppId back out of Details - see the AppId
        // resolution behavior documented in docsource/winnetsh.md and Set-NetShSslCertBinding.ps1.
        public static ResultObject BindCertificate(PSHelper psHelper, NetSHBindingInfo bindingInfo, string thumbprint, string storePath)
        {
            _logger = LogHandler.GetClassLogger(typeof(WinNetSHBinding));
            _logger.LogTrace("Attempting to bind and execute PS function (New-KeyfactorNetSHBinding)");

            // Mandatory parameters
            var parameters = new Dictionary<string, object>
            {
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
                { "Thumbprint", thumbprint },
                { "StoreName", storePath }
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }
            if (!string.IsNullOrEmpty(bindingInfo.AppId)) { parameters.Add("AppId", bindingInfo.AppId); }

            try
            {
                var results = psHelper.ExecutePowerShell("New-KeyfactorNetSHBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (New-KeyfactorNetSHBinding)");

                ResultObject result = ResultObject.FromPSResults(results);
                _logger.LogTrace($"New-KeyfactorNetSHBinding returned Status={result.Status}, Code={result.Code}, Step={result.Step}");

                if (result.Details != null && result.Details.TryGetValue("AppId", out var appId))
                {
                    _logger.LogTrace($"New-KeyfactorNetSHBinding resolved AppId={appId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"An unknown error occurred while attempting to bind thumbprint: {thumbprint} to {bindingInfo.IPAddress}:{bindingInfo.Port}. \n{ex.Message}");
            }
        }

        public static bool UnBindCertificate(PSHelper psHelper, NetSHBindingInfo bindingInfo)
        {
            _logger = LogHandler.GetClassLogger(typeof(WinNetSHBinding));
            _logger.LogTrace("Attempting to UnBind and execute PS function (Remove-KeyfactorNetSHBinding)");

            var parameters = new Dictionary<string, object>
            {
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port }
            };

            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            try
            {
                var results = psHelper.ExecutePowerShell("Remove-KeyfactorNetSHBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (Remove-KeyfactorNetSHBinding)");

                ResultObject result = ResultObject.FromPSResults(results);
                if (result.IsSuccess || string.Equals(result.Status, ResultObject.StatusSkipped, StringComparison.OrdinalIgnoreCase))
                    return true;

                _logger.LogWarning($"Remove-KeyfactorNetSHBinding returned status '{result.Status}': {result.ErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while attempting to unbind the certificate.");
                return false;
            }
        }
    }
}
