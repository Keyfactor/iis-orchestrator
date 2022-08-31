using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding.Jobs
{
    public class Management : IManagementJobExtension
    {
        private readonly ILogger<Management> _logger;

        private string _thumbprint = string.Empty;

        public Management(ILogger<Management> logger)
        {
            _logger = logger;
        }

        public string ExtensionName => "IISBindings";

        public JobResult ProcessJob(ManagementJobConfiguration jobConfiguration)
        {
            _logger.MethodEntry();
            _logger.LogTrace($"Job Configuration: {JsonConvert.SerializeObject(jobConfiguration)}");
            var complete = new JobResult
            {
                Result = OrchestratorJobStatusJobResult.Failure,
                FailureMessage =
                    "Invalid Management Operation"
            };

            switch (jobConfiguration.OperationType)
            {
                case CertStoreOperationType.Add:
                    _logger.LogTrace("Entering Add...");
                    if (jobConfiguration.JobProperties.ContainsKey("RenewalThumbprint"))
                    {
                        _thumbprint = jobConfiguration.JobProperties["RenewalThumbprint"].ToString();
                        _logger.LogTrace($"Found Thumbprint Will Renew all Certs with this thumbprint: {_thumbprint}");
                    }
                    _logger.LogTrace("Before PerformAddition...");
                    complete = PerformAddition(jobConfiguration, _thumbprint);
                    _logger.LogTrace("After PerformAddition...");
                    break;
                case CertStoreOperationType.Remove:
                    _logger.LogTrace("After PerformRemoval...");
                    complete = PerformRemoval(jobConfiguration);
                    _logger.LogTrace("After PerformRemoval...");
                    break;
            }
            _logger.MethodExit();
            return complete;
        }

        private JobResult PerformRemoval(ManagementJobConfiguration config)
        {
            try
            {
                _logger.MethodEntry();
                var siteName = config.JobProperties["Site Name"];
                var port = config.JobProperties["Port"];
                var hostName = config.JobProperties["Host Name"];
                var protocol = config.JobProperties["Protocol"];
                _logger.LogTrace($"Removing Site: {siteName}, Port:{port}, hostName:{hostName}, protocol:{protocol}");

                var storePath = JsonConvert.DeserializeObject<StorePath>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings {DefaultValueHandling = DefaultValueHandling.Populate});

                _logger.LogTrace(
                    $"Begin Removal for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");
                _logger.LogTrace($"WinRm Url: {storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman");

                var connInfo =
                    new WSManConnectionInfo(
                        new Uri($"{storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman"));
                if (storePath != null)
                {
                    _logger.LogTrace($"IncludePortInSPN: {storePath.SpnPortFlag}");
                    connInfo.IncludePortInSPN = storePath.SpnPortFlag;
                    var pw = new NetworkCredential(config.ServerUsername, config.ServerPassword)
                        .SecurePassword;
                    _logger.LogTrace($"Credentials: UserName:{config.ServerUsername} Password:{config.ServerPassword}");
                    connInfo.Credential = new PSCredential(config.ServerUsername, pw);
                    _logger.LogTrace($"PSCredential Created {pw}");
                    using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    _logger.LogTrace("runSpace Created");
                    runSpace.Open();
                    _logger.LogTrace("runSpace Opened");
                    var psCertStore = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    _logger.LogTrace("psCertStore Created");
                    using var ps = PowerShell.Create();
                    _logger.LogTrace("ps Created");
                    ps.Runspace = runSpace;
                    _logger.LogTrace("RunSpace Set");

                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    _logger.LogTrace("WebAdministration Imported");

                    ps.AddCommand("Get-WebBinding")
                        .AddParameter("Protocol", protocol)
                        .AddParameter("Name", siteName)
                        .AddParameter("Port", port)
                        .AddParameter("HostHeader", hostName)
                        .AddStatement();

                    
                    _logger.LogTrace("Get-WebBinding Set");
                    var foundBindings = ps.Invoke();
                    _logger.LogTrace("foundBindings Invoked");

                    if (foundBindings.Count == 0)
                    {
                        _logger.LogTrace($"{foundBindings.Count} Bindings Found...");
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = config.JobHistoryId,
                            FailureMessage =
                                $"Site {protocol} binding for Site {siteName} on server {config.CertificateStoreDetails.ClientMachine} not found."
                        };
                    }

                    //Log Commands out for debugging purposes
                    foreach (var cmd in ps.Commands.Commands)
                    {
                        _logger.LogTrace("Logging PowerShell Command");
                        _logger.LogTrace(cmd.CommandText);
                    }

                    ps.Commands.Clear();
                    _logger.LogTrace("Cleared Commands");

                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    _logger.LogTrace("Imported WebAdministration Module");

                    foreach (var binding in foundBindings)
                    {
                        ps.AddCommand("Remove-WebBinding")
                            .AddParameter("Name", siteName)
                            .AddParameter("BindingInformation",
                                $"{binding.Properties["bindingInformation"]?.Value}")
                            .AddStatement();
                        
                        //Log Commands out for debugging purposes
                        foreach (var cmd in ps.Commands.Commands)
                        {
                            _logger.LogTrace("Logging PowerShell Command");
                            _logger.LogTrace(cmd.CommandText);
                        }

                        var _ = ps.Invoke();
                        _logger.LogTrace("Invoked Remove-WebBinding");

                        if (ps.HadErrors)
                        {
                            _logger.LogTrace("PowerShell Had Errors");
                            var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage =
                                    $"Failed to remove {protocol} binding for Site {siteName} on server {config.CertificateStoreDetails.ClientMachine} not found, error {psError}"
                            };
                        }
                    }
                    _logger.LogTrace($"Removing Certificate with Alias: {config.JobCertificate.Alias}");
                    psCertStore.RemoveCertificate(config.JobCertificate.Alias);
                    _logger.LogTrace($"Removed Certificate with Alias: {config.JobCertificate.Alias}");
                    runSpace.Close();
                    _logger.LogTrace($"RunSpace was closed...");
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
                var failureMessage = $"Remove job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }

        private JobResult PerformAddition(ManagementJobConfiguration config,string thumpPrint)
        {
            try
            {
                _logger.MethodEntry();
                var protocol = config.JobProperties["Protocol"];
                _logger.LogTrace($"Protocol: {protocol}");

                _logger.LogTrace(
                    $"Begin Addition for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                var storePath = JsonConvert.DeserializeObject<StorePath>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings {DefaultValueHandling = DefaultValueHandling.Populate});

                _logger.LogTrace($"WinRm Url: {storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman");

                var connInfo =
                    new WSManConnectionInfo(
                        new Uri($"{storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman"));
                if (storePath != null)
                {
                    _logger.LogTrace($"IncludePortInSPN: {storePath.SpnPortFlag}");
                    connInfo.IncludePortInSPN = storePath.SpnPortFlag;
                    _logger.LogTrace($"Credentials: UserName:{config.ServerUsername} Password:{config.ServerPassword}");
                    var pw = new NetworkCredential(config.ServerUsername, config.ServerPassword)
                        .SecurePassword;
                    connInfo.Credential = new PSCredential(config.ServerUsername, pw);
                    _logger.LogTrace($"PSCredential Created {pw}");

                    _logger.LogTrace($"Creating X509 Cert from: {config.JobCertificate.Contents}");
                    var x509Cert = new X509Certificate2(
                        Convert.FromBase64String(config.JobCertificate.Contents),
                        config.JobCertificate.PrivateKeyPassword,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet |
                        X509KeyStorageFlags.Exportable);
                    _logger.LogTrace($"X509 Cert Created With Subject: {x509Cert.SubjectName}");
                    _logger.LogTrace(
                        $"Begin Add for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                    using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    _logger.LogTrace("RunSpace Created");
                    runSpace.Open();
                    _logger.LogTrace("RunSpace Opened");
                    _logger.LogTrace($"Creating Cert Store with ClientMachine: {config.CertificateStoreDetails.ClientMachine}, StorePath: {config.CertificateStoreDetails.StorePath}");
                    var _ = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    _logger.LogTrace("Cert Store Created");
                    using var ps = PowerShell.Create();
                    _logger.LogTrace("ps created");
                    ps.Runspace = runSpace;
                    _logger.LogTrace("RunSpace Assigned");

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
                    _logger.LogTrace("InstallPfxToMachineStore Statement Added...");
                    ps.AddCommand("InstallPfxToMachineStore")
                        .AddParameter("bytes", Convert.FromBase64String(config.JobCertificate.Contents))
                        .AddParameter("password", config.JobCertificate.PrivateKeyPassword)
                        .AddParameter("storeName",
                            $@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}");
                    _logger.LogTrace("InstallPfxToMachineStore Command Added...");

                    foreach (var cmd in ps.Commands.Commands)
                    {
                        _logger.LogTrace("Logging PowerShell Command");
                        _logger.LogTrace(cmd.CommandText);
                    }
                    _logger.LogTrace("Invoking ps...");
                    ps.Invoke();
                    _logger.LogTrace("ps Invoked...");
                    if (ps.HadErrors)
                    {
                        _logger.LogTrace("ps Has Errors");
                        var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = config.JobHistoryId,
                            FailureMessage =
                                $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {psError}"
                        };
                    }
                    _logger.LogTrace("Clearing Commands...");
                    ps.Commands.Clear();
                    _logger.LogTrace("Commands Cleared..");

                    //if thumbprint is there it is a renewal so we have to search all the sites for that thumbprint and renew them all
                    if (thumpPrint.Length > 0)
                    {
                        _logger.LogTrace($"Thumbprint Length > 0 {thumpPrint}");
                        ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                        _logger.LogTrace("WebAdministration Imported");
                        var searchScript = "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}"; ps.AddScript(searchScript).AddStatement();
                        _logger.LogTrace($"Search Script: {searchScript}");
                        var bindings = ps.Invoke();
                        foreach (var binding in bindings)
                        {
                            _logger.LogTrace($"Looping Bindings....");
                            var bindingSiteName = binding.Properties["name"].Value.ToString();
                            var bindingIpAddress = binding.Properties["Bindings"].Value.ToString()?.Split(':')[0];
                            var bindingPort = binding.Properties["Bindings"].Value.ToString()?.Split(':')[1];
                            var bindingHostName = binding.Properties["Bindings"].Value.ToString()?.Split(':')[2];
                            var bindingProtocol = binding.Properties["Protocol"].Value.ToString();
                            var bindingThumbprint = binding.Properties["thumbprint"].Value.ToString();
                            var bindingSniFlg = binding.Properties["sniFlg"].Value.ToString();

                            _logger.LogTrace($"bindingSiteName: {bindingSiteName}, bindingIpAddress: {bindingIpAddress}, bindingPort: {bindingPort}, bindingHostName: {bindingHostName}, bindingProtocol: {bindingProtocol}, bindingThumbprint: {bindingThumbprint}, bindingSniFlg: {bindingSniFlg}");

                            //if the thumprint of the renewal request matches the thumprint of the cert in IIS, then renew it
                            if (_thumbprint == bindingThumbprint)
                            {
                                _logger.LogTrace($"Thumbprint Match {_thumbprint}={bindingThumbprint}");
                                funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", bindingSiteName, //{0} 
                                                bindingIpAddress, //{1}
                                                bindingPort, //{2}
                                                bindingProtocol, //{3}
                                                bindingHostName, //{4}
                                                x509Cert.Thumbprint, //{5} 
                                                config.CertificateStoreDetails.StorePath, //{6}
                                                bindingSniFlg); //{7}

                                _logger.LogTrace($"funcScript {funcScript}");
                                ps.AddScript(funcScript);
                                _logger.LogTrace("funcScript added...");
                                ps.Invoke();
                                _logger.LogTrace("funcScript Invoked...");
                                foreach (var cmd in ps.Commands.Commands)
                                {
                                    _logger.LogTrace("Logging PowerShell Command");
                                    _logger.LogTrace(cmd.CommandText);
                                }
                                ps.Commands.Clear();
                                _logger.LogTrace("Commands Cleared..");
                            }
                        }
                    }
                    else
                    {
                        funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -Port ""{2}"" -Protocol ""{3}"" -HostHeader ""{4}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", config.JobProperties["Site Name"], //{0} 
                            config.JobProperties["IP Address"], //{1}
                            config.JobProperties["Port"], //{2}
                            protocol, //{3}
                            config.JobProperties["Host Name"], //{4}
                            x509Cert.Thumbprint, //{5} 
                            config.CertificateStoreDetails.StorePath, //{6}
                            Convert.ToInt16(config.JobProperties["Sni Flag"].ToString()?.Substring(0, 1))); //{7}
                        
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
                    }

                    if (ps.HadErrors)
                    {
                        var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = config.JobHistoryId,
                            FailureMessage =
                                $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: {psError}"
                        };
                    }
                    _logger.LogTrace("closing RunSpace...");
                    runSpace.Close();
                    _logger.LogTrace("RunSpace Closed...");
                }
                _logger.LogTrace("Returning Success...");
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (Exception ex)
            {
                var failureMessage = $"Add job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }
    }
}