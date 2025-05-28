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

// 021225 rcp   2.6.0   Cleaned up and verified code

using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class WinIISBinding
    {
        private static ILogger _logger;
        private static Collection<PSObject>? _results = null;
        private static PSHelper _helper;

        public static Collection<PSObject> BindCertificate(PSHelper psHelper, IISBindingInfo bindingInfo, string thumbprint, string renewalThumbprint, string storePath)
        {
            _logger = LogHandler.GetClassLogger(typeof(WinIISBinding));
            _logger.LogTrace("Attempting to bind and execute PS function (New-KFIISSiteBinding)");

            // Mandatory parameters
            var parameters = new Dictionary<string, object>
            {
                { "SiteName", bindingInfo.SiteName },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
                { "Protocol", bindingInfo.Protocol },
                { "Thumbprint", thumbprint },
                { "StoreName", storePath },
                { "SslFlags", bindingInfo.SniFlag }
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            try
            {
                return psHelper.ExecutePowerShell("New-KFIISSiteBinding", parameters);      // returns true if successful
            }
            catch (Exception ex)
            {
                throw new Exception($"An unknown error occurred while attempting to bind thumbprint: {thumbprint} to site: '{bindingInfo.SiteName}'. \n{ex.Message}");
            }
        }

        public static bool UnBindCertificate(PSHelper psHelper, IISBindingInfo bindingInfo)
        {
            _logger = LogHandler.GetClassLogger(typeof(WinIISBinding));
            _logger.LogTrace("Attempting to UnBind and execute PS function (Remove-KFIISSiteBinding)");

            // Mandatory parameters
            var parameters = new Dictionary<string, object>
            {
                { "SiteName", bindingInfo.SiteName },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName))
            {
                parameters.Add("HostName", bindingInfo.HostName);
            }

            try
            {
                var results = psHelper.ExecutePowerShell("Remove-KFIISSiteBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (Remove-KFIISSiteBinding)");

                if (results == null || results.Count == 0)
                {
                    _logger.LogWarning("PowerShell function returned no results.");
                    return false;
                }

                if (results[0].BaseObject is bool success)
                {
                    return success;
                }
                else
                {
                    _logger.LogWarning("Unexpected result type from PowerShell function.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while attempting to unbind the certificate.");
                return false;
            }
        }
    }
}
