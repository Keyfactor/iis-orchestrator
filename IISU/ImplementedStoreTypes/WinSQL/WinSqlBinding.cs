// Ignore Spelling: Keyfactor Sql

using Keyfactor.Logging;
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

        public WinSqlBinding()
        {
            _logger = LogHandler.GetClassLogger<Management>();
            _logger.MethodEntry();
        }

        public static bool BindSQLCertificate(PSHelper psHelper, string SQLInstanceNames, string newThumbprint, string renewalThumbprint, string storePath, bool restartSQLService)
        {
            _logger = LogHandler.GetClassLogger<Management>();
            _logger.MethodEntry();

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "SQLInstance", SQLInstanceNames },
                    { "RenewalThumbprint", renewalThumbprint.ToLower() },
                    { "NewThumbprint", newThumbprint.ToLower() }
                };

                if (restartSQLService)
                {
                    parameters["RestartService"] = restartSQLService;
                }

                _results = psHelper.ExecutePowerShell("Bind-KFSqlCertificate", parameters);
                if (_results != null && _results.Count > 0)
                {
                    // Extract value from PSObject and convert to bool
                    if (bool.TryParse(_results[0]?.BaseObject?.ToString(), out bool result))
                    {
                        _logger.LogTrace($"PowerShell function Bind-KFSqlCertificate returned: {result}");
                        return result;
                    }
                }

                _logger.LogWarning("PowerShell function Bind-KFSqlCertificate did not return a valid boolean result.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing PowerShell function: Bind-KFSqlCertificate");
                return false;
            }
        }

        public static bool UnBindSQLCertificate(PSHelper psHelper, string SQLInstanceNames, bool restartSQLService)
        {
            _logger = LogHandler.GetClassLogger<Management>();
            _logger.MethodEntry();

            _logger.LogTrace("Entered method UnBindSQLCertificate");
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "SQLInstanceNames", SQLInstanceNames.Trim() } // Send full list at once
                };

                if (restartSQLService)
                {
                    parameters["RestartService"] = restartSQLService;
                }

                _results = psHelper.ExecutePowerShell("Unbind-KFSqlCertificate", parameters);
                if (_results != null && _results.Count > 0)
                {
                    if (bool.TryParse(_results[0]?.BaseObject?.ToString(), out bool result))
                    {
                        _logger.LogTrace($"PowerShell function Unbind-KFSqlCertificate returned: {result}");
                        return result;
                    }
                }

                _logger.LogWarning("PowerShell function Unbind-KFSqlCertificate did not return a valid boolean result.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while unbinding certificate(s) from SQL instance(s)");
                return false;
            }
        }
    }
}
