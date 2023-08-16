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
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPsSqlManager
    {
        private string SqlServiceUser { get; set; }
        private string SqlInstanceName { get; set; }
        private string SqlServerServiceName { get; set; }
        private bool RestartService { get; set; }
        private string RegistryPath { get; set; }
        private string RenewalThumbprint { get; set; } = "";
        private string ClientMachineName { get; set; }
        private long JobHistoryID { get; set; }
        private readonly ILogger _logger;
        private readonly Runspace _runSpace;

        private PowerShell ps;

        public ClientPsSqlManager(ManagementJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<ClientPSIIManager>();

            try
            {
                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                JobHistoryID = config.JobHistoryId;

                if (config.JobProperties.ContainsKey("RenewalThumbprint"))
                {
                    RenewalThumbprint = config.JobProperties["RenewalThumbprint"].ToString();
                    _logger.LogTrace($"Found Thumbprint Will Renew all Certs with this thumbprint: {RenewalThumbprint}");
                }

                if (config.JobProperties.ContainsKey("InstanceName") && config.JobProperties.ContainsKey("RestartService"))
                {
                    SqlInstanceName = config.JobProperties["InstanceName"].ToString();
                    RestartService = Convert.ToBoolean(config.JobProperties["RestartService"].ToString());
                }

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = PsHelper.GetClientPsRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating a SQL Management Job: {e.Message}", e.InnerException);
            }
        }

        public JobResult UnBindCertificate()
        {
            try
            {
                _logger.MethodEntry();

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;

                RegistryPath = GetSqlCertRegistryLocation(SqlInstanceName, ps);

                var funcScript = string.Format($"Clear-ItemProperty -Path \"{RegistryPath}\" -Name Certificate");
                foreach (var cmd in ps.Commands.Commands)
                {
                    _logger.LogTrace("Logging PowerShell Command");
                    _logger.LogTrace(cmd.CommandText);
                }

                _logger.LogTrace($"funcScript {funcScript}");
                ps.AddScript(funcScript);
                _logger.LogTrace("funcScript added...");
                ps.Invoke();
                _logger.LogTrace("funcScript Invoked...");

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error.ErrorDetails.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage = $"Unable to unbind certificate to Sql Server"
                        };
                    }
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Error Occurred in unbind {LogHandler.FlattenException(e)}"
                };
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }
        }

        private string GetSqlInstanceValue(string instanceName,PowerShell ps)
        {
            try
            {
                var funcScript = string.Format(@$"Get-ItemPropertyValue ""HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"" -Name {instanceName}");
                foreach (var cmd in ps.Commands.Commands)
                {
                    _logger.LogTrace("Logging PowerShell Command");
                    _logger.LogTrace(cmd.CommandText);
                }

                _logger.LogTrace($"funcScript {funcScript}");
                ps.AddScript(funcScript);
                _logger.LogTrace("funcScript added...");
                var SqlInstanceValue = ps.Invoke()[0].ToString();
                _logger.LogTrace("funcScript Invoked...");
                ps.Commands.Clear();

                if (!ps.HadErrors)
                {
                    return SqlInstanceValue;
                }
                return null;
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating getting instance name from registry: {e.Message}", e.InnerException);
            }
        }

         private string GetSqlCertRegistryLocation(string instanceName,PowerShell ps)
        {
            return $"HKLM:\\SOFTWARE\\Microsoft\\Microsoft SQL Server\\{GetSqlInstanceValue(instanceName,ps)}\\MSSQLServer\\SuperSocketNetLib\\";
        }

        private string GetSqlServerServiceName(string instanceValue)
        {
            if(string.IsNullOrEmpty(instanceValue))
                return string.Empty;

            //Default SQL Instance has this format
            if (instanceValue.Split('.')[1] == "MSSQLSERVER")
                return "MSSQLSERVER";

            //Named Instance service has this format
            return $"MSSQL`${instanceValue.Split('.')[1]}";
        }

        public JobResult BindCertificate(X509Certificate2 x509Cert)
        {
            try
            {
                _logger.MethodEntry();

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;

                RegistryPath = GetSqlCertRegistryLocation(SqlInstanceName, ps);

                var thumbPrint = string.Empty;
                if (x509Cert != null)
                    thumbPrint = x509Cert.Thumbprint.ToLower(); //sql server config mgr expects lower

                var funcScript = string.Format($"Set-ItemProperty -Path \"{RegistryPath}\" -Name Certificate {thumbPrint}");
                foreach (var cmd in ps.Commands.Commands)
                {
                    _logger.LogTrace("Logging PowerShell Command");
                    _logger.LogTrace(cmd.CommandText);
                }

                _logger.LogTrace($"funcScript {funcScript}");
                ps.AddScript(funcScript);
                _logger.LogTrace("funcScript added...");
                ps.Invoke();
                _logger.LogTrace("funcScript Invoked...");

                _logger.LogTrace("Setting up Acl Access for Manage Private Keys");
                ps.Commands.Clear();

                //Get the SqlServer Service User Name
                var serviceName = GetSqlServerServiceName(GetSqlInstanceValue(SqlInstanceName, ps));
                funcScript = @$"(Get-WmiObject Win32_Service -Filter ""Name='{serviceName}'"").StartName";
                ps.AddScript(funcScript);
                _logger.LogTrace("funcScript added...");
                SqlServiceUser = ps.Invoke()[0].ToString();
                _logger.LogTrace("funcScript Invoked...");
                _logger.LogTrace("Got service login user for ACL Permissions");
                ps.Commands.Clear();

                funcScript = $@"$thumbprint = '{thumbPrint}'
                    $Cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {{ $_.Thumbprint -eq $thumbprint }}
                    $privKey = $Cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName 
                    $keyPath = ""$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\""
                    $privKeyPath = (Get-Item ""$keyPath\$privKey"")
                    $Acl = Get-Acl $privKeyPath
                    $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule(""{SqlServiceUser.Replace("$","`$")}"", ""Read"", ""Allow"")
                    $Acl.SetAccessRule($Ar)
                    Set-Acl $privKeyPath.FullName $Acl";

                ps.AddScript(funcScript);
                ps.Invoke();
                _logger.LogTrace("ACL FuncScript Invoked...");

                //If user filled in a service name in the store then restart the SQL Server Services
                if (RestartService)
                {
                    _logger.LogTrace("Starting to Restart SQL Server Service...");
                    ps.Commands.Clear();
                    funcScript = $@"Restart-Service -Name ""{serviceName}"" -Force";

                    ps.AddScript(funcScript);
                    ps.Invoke();
                    _logger.LogTrace("Invoked Restart SQL Server Service....");
                }

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error?.Exception.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage =$"Unable to bind certificate to Sql Server"
                        };
                    }
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Error Occurred in InstallCertificate {LogHandler.FlattenException(e)}"
                };
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }
        }
    }
}

