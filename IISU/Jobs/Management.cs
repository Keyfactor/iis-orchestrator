using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISU.Jobs
{
    public class Management : IManagementJobExtension
    {
        private ILogger _logger;

        private IPAMSecretResolver _resolver;

        private string _thumbprint = string.Empty;

        private string ServerUserName { get; set; }
        private string ServerPassword { get; set; }

        public Management(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        public string ExtensionName => "IISU";

        private string ResolvePamField(string name,string value)
        {
            _logger.LogTrace($"Attempting to resolved PAM eligible field {name}");
            return _resolver.Resolve(value);
        }

        public JobResult ProcessJob(ManagementJobConfiguration jobConfiguration)
        {
            _logger = LogHandler.GetClassLogger<Management>();
            ServerUserName = ResolvePamField("Server UserName", jobConfiguration.ServerUsername);
            ServerPassword = ResolvePamField("Server Password", jobConfiguration.ServerPassword);
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
                    complete = PerformAddition(jobConfiguration);
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

                var storePath = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
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
                    var pw = new NetworkCredential(ServerUserName, ServerPassword)
                        .SecurePassword;
                    _logger.LogTrace($"Credentials: UserName:{ServerUserName} Password:{ServerPassword}");
                    connInfo.Credential = new PSCredential(ServerUserName, pw);
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

        private JobResult PerformAddition(ManagementJobConfiguration config)
        {
            try
            {
                _logger.MethodEntry();
                
                    var iisManager=new IISManager(config,ServerUserName,ServerPassword);
                    return iisManager.AddCertificate();
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