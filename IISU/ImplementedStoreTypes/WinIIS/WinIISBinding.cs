using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web.Services.Description;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class WinIISBinding
    {
        private static ILogger _logger;
        private static Collection<PSObject>? _results = null;
        private static PSHelper _helper;

        public static void BindCertificate(PSHelper psHelper, IISBindingInfo bindingInfo, string thumbprint, string renewalThumbprint, string storePath)
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

            _results = psHelper.ExecutePowerShell("New-KFIISSiteBinding", parameters);      // returns true if successful
            _logger.LogTrace("Returned from executing PS function (New-KFIISSiteBinding)");

            // This should return the thumbprint of the certificate
            if (_results != null && _results.Count > 0)
            {
                bool success = _results[0].BaseObject is bool value && value;
                if (success)
                {
                    _logger.LogTrace($"Bound certificate with the thumbprint: '{thumbprint}' to site: '{bindingInfo.SiteName}' successfully.");
                    return;
                }
                else
                {
                    _logger.LogTrace("Something happened and the binding failed.");
                }
            }

            throw new Exception($"An unknown error occurred while attempting to bind thumbprint: {thumbprint} to site: '{bindingInfo.SiteName}'. \nCheck the UO Logs for more information.");
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
            if (!string.IsNullOrEmpty(bindingInfo.HostName))
            {
                parameters.Add("HostName", bindingInfo.HostName);
            }

            try
            {
                var results = psHelper.ExecutePowerShell("Remove-KFIISBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (Remove-KFIISBinding)");

                if (results == null || results.Count == 0)
                {
                    _logger.LogWarning("PowerShell function returned no results.");
                    return false;
                }

                if (results[0].BaseObject is bool success)
                {
                    _logger.LogTrace($"Returned from unbinding as {success}.");
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
