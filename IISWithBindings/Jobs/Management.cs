using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding.Jobs
{
    public class Management : IManagementJobExtension
    {
        private readonly ILogger<Management> _logger;

        public Management(ILogger<Management> logger)
        {
            _logger = logger;
        }

        public string ExtensionName => "IISBindings";

        public JobResult ProcessJob(ManagementJobConfiguration jobConfiguration)
        {
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage =
                    "Invalid Management Operation"
            };

            switch (jobConfiguration.OperationType)
            {
                case CertStoreOperationType.Add:
                    if (jobConfiguration.JobProperties.ContainsKey("RenewalThumbprint"))
                    {
                        complete = PerformRenewal(jobConfiguration);
                    }
                    else
                    {
                        complete = PerformAddition(jobConfiguration);
                    }
                    break;
                case CertStoreOperationType.Remove:
                    complete = PerformRemoval(jobConfiguration);
                    break;
            }

            return complete;
        }

        private JobResult PerformRemoval(ManagementJobConfiguration config)
        {
            try
            {
                var storePath = JsonConvert.DeserializeObject<StorePath>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings {DefaultValueHandling = DefaultValueHandling.Populate});

                _logger.LogTrace(
                    $"Begin Removal for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                var connInfo =
                    new WSManConnectionInfo(
                        new Uri($"http://{config.CertificateStoreDetails.ClientMachine}:5985/wsman"));
                if (storePath != null)
                {
                    connInfo.IncludePortInSPN = storePath.SpnPortFlag;
                    var pw = new NetworkCredential(config.ServerUsername, config.ServerPassword)
                        .SecurePassword;
                    connInfo.Credential = new PSCredential(config.ServerUsername, pw);

                    using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    runSpace.Open();
                    var psCertStore = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    using var ps = PowerShell.Create();
                    ps.Runspace = runSpace;

                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    ps.AddCommand("Get-WebBinding")
                        .AddParameter("Protocol", storePath.Protocol)
                        .AddParameter("Name", storePath.SiteName)
                        .AddParameter("Port", storePath.Port)
                        .AddParameter("HostHeader", storePath.HostName)
                        .AddStatement();
                    //ps.AddScript($"(Get-WebBinding -Protocol {storePath.Protocol} -Name {storePath.SiteName} -Port {storePath.Port} -HostHeader {storePath.HostName})").AddStatement();

                    var foundBindings = ps.Invoke();
                    if (foundBindings.Count == 0)
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            FailureMessage =
                                $"Site {storePath.Protocol} binding for Site {storePath.SiteName} on server {config.CertificateStoreDetails.ClientMachine} not found."
                        };

                    ps.Commands.Clear();
                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();
                    foreach (var binding in foundBindings)
                    {
                        ps.AddCommand("Remove-WebBinding")
                            .AddParameter("Name", storePath.SiteName)
                            .AddParameter("BindingInformation",
                                $"{binding.Properties["bindingInformation"]?.Value}")
                            .AddStatement();
                        var _ = ps.Invoke();
                        if (ps.HadErrors)
                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                FailureMessage =
                                    $"Failed to remove {storePath.Protocol} binding for Site {storePath.SiteName} on server {config.CertificateStoreDetails.ClientMachine} not found."
                            };
                    }

                    psCertStore.RemoveCertificate(config.JobCertificate.Alias);
                    runSpace.Close();
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex.Message);
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    FailureMessage =
                        $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {ex.Message}"
                };
            }
        }

        private JobResult PerformAddition(ManagementJobConfiguration config)
        {
            try
            {
                var storePath = JsonConvert.DeserializeObject<StorePath>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings {DefaultValueHandling = DefaultValueHandling.Populate});

                var connInfo =
                    new WSManConnectionInfo(
                        new Uri($"http://{config.CertificateStoreDetails.ClientMachine}:5985/wsman"));
                if (storePath != null)
                {
                    connInfo.IncludePortInSPN = storePath.SpnPortFlag;
                    var pw = new NetworkCredential(config.ServerUsername, config.ServerPassword)
                        .SecurePassword;
                    connInfo.Credential = new PSCredential(config.ServerUsername, pw);

                    var x509Cert = new X509Certificate2(
                        Convert.FromBase64String(config.JobCertificate.Contents),
                        config.JobCertificate.PrivateKeyPassword,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet |
                        X509KeyStorageFlags.Exportable);

                    _logger.LogTrace(
                        $"Begin Add for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                    using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    runSpace.Open();
                    var _ = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    using var ps = PowerShell.Create();
                    ps.Runspace = runSpace;

                    var funcScript = @"
                                                    $ErrorActionPreference = ""Stop""

                                                    function InstallPfxToMachineStore([byte[]]$bytes, [string]$password, [string]$storeName) {
                                                        $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, ""LocalMachine""
                                                        $certStore.Open(5)
                                                        $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $bytes, $password, 18 <# Persist, Machine #>
                                                        $certStore.Add($cert)
                                                        $certStore.Close();
                                                    }";

                    ps.AddScript(funcScript).AddStatement();
                    ps.AddCommand("InstallPfxToMachineStore")
                        .AddParameter("bytes", Convert.FromBase64String(config.JobCertificate.Contents))
                        .AddParameter("password", config.JobCertificate.PrivateKeyPassword)
                        .AddParameter("storeName",
                            $@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}");

                    ps.Invoke();

                    if (ps.HadErrors)
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            FailureMessage =
                                $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}"
                        };

                    ps.Commands.Clear();
                    funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -Port ""{2}"" -Protocol ""{3}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", storePath.SiteName, //{0} 
                        config.JobProperties["IP Address"], //{1}
                        config.JobProperties["Port"], //{2}
                        storePath.Protocol, //{3}
                        config.JobProperties["IP Address"], //{4}
                        x509Cert.Thumbprint, //{5} 
                        config.CertificateStoreDetails.StorePath, //{6}
                        Convert.ToInt16(config.JobProperties["Sni Flag"].ToString().Substring(0,1))); //{7}

                    ps.AddScript(funcScript);
                    ps.Invoke();

                    if (ps.HadErrors)
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            FailureMessage =
                                $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}"
                        };

                    runSpace.Close();
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex.Message);
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    FailureMessage =
                        $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {ex.Message}"
                };
            }
        }
    }
}