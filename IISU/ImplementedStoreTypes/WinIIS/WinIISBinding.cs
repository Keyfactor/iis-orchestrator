using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class WinIISBinding
    {
        private static ILogger _logger;
        private static Collection<PSObject>? _results = null;
        private static PSHelper _helper;

        public static void BindCertificate(PSHelper psHelper, IISBindingInfo bindingInfo, string thumbprint, string renewalThumbprint, string storePath)
        {
            _logger.LogTrace("Attempting to bind and execute PS function (New-KFIISSiteBinding)");

            // Mandatory parameters
            var parameters = new Dictionary<string, object>
            {
                { "Thumbprint", thumbprint },
                { "WebSite", bindingInfo.SiteName },
                { "Protocol", bindingInfo.Protocol },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
                { "SNIFlag", bindingInfo.SniFlag },
                { "StoreName", storePath },
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            _results = psHelper.ExecutePowerShell("New-KFIISSiteBinding", parameters);
            _logger.LogTrace("Returned from executing PS function (Add-KFCertificateToStore)");

            // This should return the thumbprint of the certificate
            if (_results != null && _results.Count > 0)
            {
                _logger.LogTrace($"Bound certificate with the thumbprint: '{thumbprint}' to site: '{bindingInfo.SiteName}'.");
            }
            else
            {
                _logger.LogTrace("No results were returned.  There could have been an error while adding the certificate.  Look in the trace logs for PowerShell informaiton.");
            }
        }

        public static bool UnBindCertificate(PSHelper psHelper, IISBindingInfo bindingInfo)
        {
            _logger.LogTrace("Attempting to UnBind and execute PS function (Remove-KFIISBinding)");

            // Mandatory parameters
            var parameters = new Dictionary<string, object>
            {
                { "SiteName", bindingInfo.SiteName },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            try
            {
                _results = psHelper.ExecutePowerShell("Remove-KFIISBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (Remove-KFIISBinding)");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
