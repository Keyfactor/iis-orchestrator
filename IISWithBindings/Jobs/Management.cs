using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Interfaces;
using Microsoft.Web.Administration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding2.Jobs
{
    [Job(JobTypes.MANAGEMENT)]
    public class Management: AgentJob, IAgentJobExtension
    {
        public override AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {

            AnyJobCompleteInfo complete = new AnyJobCompleteInfo()
            {
                Status = 4,
                Message = "Invalid Management Operation"
            };

            switch (config.Job.OperationType)
            {
                case AnyJobOperationType.Add:
                    complete = PerformAddition(config);
                    break;
                case AnyJobOperationType.Remove:
                    complete = PerformRemoval(config);
                    break;
            }
            return complete;
        }

        private AnyJobCompleteInfo PerformRemoval(AnyJobConfigInfo config)
        {
            try
            {
                AnyJobCompleteInfo complete = new AnyJobCompleteInfo()
                {
                    Status = 4,
                    Message = "Invalid Management Operation"
                };
                dynamic properties = JsonConvert.DeserializeObject(config.Store.Properties.ToString());
                StorePath storePath = new StorePath(properties.siteName.Value, properties.ipAddress.Value, properties.port.Value, properties.hostName.Value);

                Logger.Trace($"Begin Removal for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

                using (X509Store certStore = new X509Store($@"\\{config.Store.ClientMachine}\{config.Store.StorePath}", StoreLocation.LocalMachine))
                {
                    certStore.Open(OpenFlags.MaxAllowed);
                    X509Certificate2 cert = certStore.Certificates.Cast<X509Certificate2>().FirstOrDefault(p => p.Thumbprint == config.Job.Alias);
                    if (cert != null)
                    {
                        using (ServerManager serverManager = ServerManager.OpenRemote(config.Store.ClientMachine))
                        {
                            Site site = serverManager.Sites[storePath.SiteName];
                            if (site == null)
                            {
                                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine} not found." };
                            }

                            List<Binding> existingBindings = site.Bindings.Where(p => p.Protocol.Equals("https", StringComparison.CurrentCultureIgnoreCase) &&
                                                                                 p.BindingInformation.Equals(storePath.FormatForIIS(), StringComparison.CurrentCultureIgnoreCase)).ToList();
                            foreach (Binding binding in existingBindings)
                            {
                                site.Bindings.Remove(binding);
                            }

                            serverManager.CommitChanges();
                        }

                        certStore.Remove(cert);
                        
                    }
                }
                return new AnyJobCompleteInfo { Status = 2, Message = "" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }

        private AnyJobCompleteInfo PerformAddition(AnyJobConfigInfo config)
        {
            try
            {
                Logger.Trace($"Begin Add for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

                using (X509Store certStore = new X509Store($@"\\{config.Store.ClientMachine}\{config.Store.StorePath}", StoreLocation.LocalMachine))
                {

                    dynamic properties = JsonConvert.DeserializeObject(config.Store.Properties.ToString());
                    StorePath storePath = new StorePath(properties.siteName.Value, properties.ipAddress.Value, properties.port.Value, properties.hostName.Value);

                    X509Certificate2 x509Cert = new X509Certificate2(Convert.FromBase64String(config.Job.EntryContents), config.Job.PfxPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                    using (Runspace runspace = RunspaceFactory.CreateRunspace(new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"))))
                    {
                        runspace.Open();
                        using (PowerShell ps = PowerShell.Create())
                        {
                            ps.Runspace = runspace;

                            string funcScript = @"
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
                                .AddParameter("bytes", Convert.FromBase64String(config.Job.EntryContents))
                                .AddParameter("password", config.Job.PfxPassword)
                                .AddParameter("storeName", $@"\\{config.Store.ClientMachine}\{config.Store.StorePath}");

                            ps.Invoke();

                            if (ps.HadErrors)
                            {
                                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}" };
                            }
                        }
                        runspace.Close();
                    }

                    using (Runspace runspace = RunspaceFactory.CreateRunspace(new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"))))
                    {
                        runspace.Open();
                        using (PowerShell ps = PowerShell.Create())
                        {
                            ps.Runspace = runspace;

                            string funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -Port ""{2}"" -Protocol ""{3}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}""
                                                Get-WebBinding -Name ""{0}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", storePath.SiteName, storePath.IP, storePath.Port, storePath.Protocol, storePath.HostName, x509Cert.Thumbprint, config.Store.StorePath);

                            ps.AddScript(funcScript);
                            ps.Invoke();

                            if (ps.HadErrors)
                            {
                                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}" };
                            }
                        }
                        runspace.Close();
                    }
                }

                return new AnyJobCompleteInfo() { Status = 2, Message = "Addition of certificate and binding complete" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }
    }
}
