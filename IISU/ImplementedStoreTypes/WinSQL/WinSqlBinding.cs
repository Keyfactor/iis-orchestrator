// Ignore Spelling: Keyfactor Sql

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql
{
    public class WinSqlBinding
    {
        private static ILogger _logger;
        private static Collection<PSObject>? _results = null;

        public static bool BindSQLCertificate(PSHelper psHelper, string SQLInstanceNames, string newThumbprint, string renewalThumbprint, string storePath, bool restartSQLService)
        {
            bool hadError = false;
            var instances = SQLInstanceNames.Split(",");

            foreach (var instanceName in instances)
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Thumbprint", newThumbprint },
                    { "SqlInstanceName", instanceName.Trim() },
                    { "StoreName", storePath },
                    { "RestartService", restartSQLService }
                };

                try
                {
                    _results = psHelper.ExecutePowerShell("Bind-CertificateToSqlInstance", parameters);
                    _logger.LogTrace("Return from executing PS function (Bind-CertificateToSqlInstance)");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error occurred while binding certificate to SQL Instance {instanceName}", ex);
                    hadError = true;
                }
            }

            if (hadError) return false;
            else return true;
        }

        public static bool UnBindSQLCertificate(PSHelper psHelper, string SQLInstanceNames)
        {
            bool hadError = false;
            var instances = SQLInstanceNames.Split(",");

            foreach (var instanceName in instances)
            {
                var parameters = new Dictionary<string, object>
                {
                    { "SqlInstanceName", instanceName.Trim() }
                };

                try
                {
                    _results = psHelper.ExecutePowerShell("UnBind-KFSqlServerCertificate", parameters);
                    _logger.LogTrace("Returned from executing PS function (UnBind-KFSqlServerCertificate)");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error occurred while binding certificate to SQL Instance {instanceName}", ex);
                    hadError = true;
                }
            }

            if (hadError) return false;
            else return true;
        }
    }
}
