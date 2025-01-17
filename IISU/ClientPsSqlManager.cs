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
using Microsoft.Management.Infrastructure.Serialization;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Web.Services.Description;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPsSqlManager
    {
        private string SqlServiceUser { get; set; }
        private string SqlInstanceName { get; set; }
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
            _logger = LogHandler.GetClassLogger<ClientPsSqlManager>();

            try
            {
                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                JobHistoryID = config.JobHistoryId;

                if (config.JobProperties.ContainsKey("InstanceName"))
                {
                    var instanceRef = config.JobProperties["InstanceName"]?.ToString();
                    SqlInstanceName = string.IsNullOrEmpty(instanceRef) ? "MSSQLSERVER":instanceRef;
                }

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;
                RestartService = jobProperties.RestartService;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = PsHelper.GetClientPsRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating a SQL Management Job: {e.Message}", e.InnerException);
            }
        }

        public ClientPsSqlManager(InventoryJobConfiguration config,Runspace runSpace)
        {
            _logger = LogHandler.GetClassLogger<ClientPsSqlManager>();

            try
            {
                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                JobHistoryID = config.JobHistoryId;

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = runSpace;
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating a SQL Inventory Job: {e.Message}", e.InnerException);
            }
        }

        public ClientPsSqlManager(ReenrollmentJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<ClientPsSqlManager>();

            try
            {
                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                JobHistoryID = config.JobHistoryId;

                if (config.JobProperties.ContainsKey("InstanceName"))
                {
                    var instanceRef = config.JobProperties["InstanceName"]?.ToString();
                    SqlInstanceName = string.IsNullOrEmpty(instanceRef) ? "MSSQLSERVER" : instanceRef;
                }

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;
                RestartService = jobProperties.RestartService;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = PsHelper.GetClientPsRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating a SQL ReEnrollment Job: {e.Message}", e.InnerException);
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

        public string GetSqlInstanceValue(string instanceName,PowerShell ps)
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
            catch (ArgumentOutOfRangeException)
            {
                throw new Exception($"There were no SQL instances with the name: {instanceName}.  Please check the spelling of the SQL instance.");
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating getting instance name from registry: {e.Message}", e.InnerException);
            }
        }

        public string GetSqlCertRegistryLocation(string instanceName,PowerShell ps)
        {
            return $"HKLM:\\SOFTWARE\\Microsoft\\Microsoft SQL Server\\{GetSqlInstanceValue(instanceName,ps)}\\MSSQLServer\\SuperSocketNetLib\\";
        }

        public string GetSqlServerServiceName(string instanceName)
        {
            if(string.IsNullOrEmpty(instanceName))
                return string.Empty;

            //Default SQL Instance has this format
            if (instanceName == "MSSQLSERVER")
                return "MSSQLSERVER";

            //Named Instance service has this format
            return $"MSSQL`${instanceName}";
        }

        public JobResult BindCertificates(string renewalThumbprint, X509Certificate2 x509Cert)
        {
            try
            {
                var bindingError = string.Empty;
                RenewalThumbprint = renewalThumbprint;

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;
                if (!string.IsNullOrEmpty(renewalThumbprint))
                {
                    var funcScript = string.Format(@$"(Get-ItemProperty ""HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server"").InstalledInstances");
                    ps.AddScript(funcScript);
                    _logger.LogTrace("funcScript added...");
                    var instances = ps.Invoke();
                    ps.Commands.Clear();
                    foreach (var instance in instances)
                    {
                        var regLocation = GetSqlCertRegistryLocation(instance.ToString(), ps);

                        funcScript = string.Format(@$"Get-ItemPropertyValue ""{regLocation}"" -Name Certificate");
                        ps.AddScript(funcScript);
                        _logger.LogTrace("funcScript added...");
                        var thumbprint = ps.Invoke()[0].ToString();
                        ps.Commands.Clear();

                        if (RenewalThumbprint.Contains(thumbprint, StringComparison.CurrentCultureIgnoreCase))
                        {
                            bindingError=BindCertificate(x509Cert, ps);
                        }
                    }
                }
                else
                {
                    bindingError=BindCertificate(x509Cert, ps);
                }

                if (bindingError.Length == 0)
                {
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Success,
                        JobHistoryId = JobHistoryID,
                        FailureMessage = ""
                    };
                }
                else
                {
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Failure,
                        JobHistoryId = JobHistoryID,
                        FailureMessage = bindingError
                    };
                }

            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Error Occurred in BindCertificates {LogHandler.FlattenException(e)}"
                };
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }

        }
        public string BindCertificate(X509Certificate2 x509Cert,PowerShell ps)
        {
            try
            {
                _logger.MethodEntry();


                //If they comma separated the instance entry param, they are trying to install to more than 1 instance
                var instances = SqlInstanceName.Split(',');

                foreach (var instanceName in instances)
                {
                    RegistryPath = GetSqlCertRegistryLocation(instanceName, ps);

                    var thumbPrint = string.Empty;
                    if (x509Cert != null)
                        thumbPrint = x509Cert.Thumbprint.ToLower(); //sql server config mgr expects lower

                    var funcScript = string.Format($"Set-ItemProperty -Path \"{RegistryPath}\" -Name Certificate {thumbPrint}");
                    foreach (var cmd in ps.Commands.Commands)
                    {
                        _logger.LogTrace("Logging PowerShell Command");
                        _logger.LogTrace(cmd.CommandText);
                    }

                    ps.AddScript(funcScript);
                    _logger.LogTrace($"Running script: {funcScript}");
                    ps.Invoke();
                    _logger.LogTrace("funcScript Invoked...");

                    _logger.LogTrace("Setting up Acl Access for Manage Private Keys");
                    ps.Commands.Clear();

                    //Get the SqlServer Service User Name
                    var serviceName = GetSqlServerServiceName(instanceName);
                    if (serviceName != "")
                    {
                        _logger.LogTrace($"Service Name: {serviceName} was returned.");

                        funcScript = @$"(Get-WmiObject Win32_Service -Filter ""Name='{serviceName}'"").StartName";
                        ps.AddScript(funcScript);
                        _logger.LogTrace($"Running script: {funcScript}");
                        SqlServiceUser = ps.Invoke()[0].ToString();

                        _logger.LogTrace($"SqlServiceUser: {SqlServiceUser}");
                        _logger.LogTrace("Got service login user for ACL Permissions");
                        ps.Commands.Clear();

                        funcScript = $@"$thumbprint = '{thumbPrint}'
                    $Cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {{ $_.Thumbprint -eq $thumbprint }}
                    $privKey = $Cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName 
                    $keyPath = ""$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\""
                    $privKeyPath = (Get-Item ""$keyPath\$privKey"")
                    $Acl = Get-Acl $privKeyPath
                    $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule(""{SqlServiceUser.Replace("$", "`$")}"", ""Read"", ""Allow"")
                    $Acl.SetAccessRule($Ar)
                    Set-Acl $privKeyPath.FullName $Acl";

                        ps.AddScript(funcScript);
                        ps.Invoke();
                        _logger.LogTrace("ACL FuncScript Invoked...");

                    }
                    else 
                    {
                        _logger.LogTrace("No Service User has been returned.  Skipping ACL update.");
                    }

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
                            return psError;
                        }
                    }
                }
                return "";
            }
            catch (Exception e)
            {
                return LogHandler.FlattenException(e);
            }

        }
    }
}

