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
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPSCertStoreManager
    {
        private ILogger _logger;
        private Runspace _runspace;
        private long _jobNumber = 0;

        private X509Certificate2 x509Cert;

        public X509Certificate2 X509Cert
        {
            get { return x509Cert; }
        }


        public ClientPSCertStoreManager(ILogger logger, Runspace runSpace, long jobNumber)
        {
            _logger = logger;
            _runspace = runSpace;
            _jobNumber = jobNumber;
        }

        public string CreatePFXFile(string certificateContents, string privateKeyPassword)
        {
            try
            {
                // Create the x509 certificate
                //x509Cert = new X509Certificate2
                //    (
                //        Convert.FromBase64String(certificateContents),
                //        privateKeyPassword,
                //        X509KeyStorageFlags.MachineKeySet |
                //        X509KeyStorageFlags.PersistKeySet |
                //        X509KeyStorageFlags.Exportable
                //    );

                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;

                    // Add script to write certificate contents to a temporary file
                    string script = @"
                            param($certificateContents)
                            $filePath = [System.IO.Path]::GetTempFileName() + '.pfx'
                            [System.IO.File]::WriteAllBytes($filePath, [System.Convert]::FromBase64String($certificateContents))
                            $filePath
                            ";

                    ps.AddScript(script);
                    ps.AddParameter("certificateContents", certificateContents); // Convert.ToBase64String(x509Cert.Export(X509ContentType.Pkcs12)));

                    // Invoke the script on the remote computer
                    var results = ps.Invoke();

                    // Get the result (temporary file path) returned by the script
                    return results[0].ToString();
                }
            }
            catch (Exception)
            {
                throw new Exception("An error occurred while attempting to create and write the X509 contents.");
            }
        }

        public void DeletePFXFile(string filePath, string fileName)
        {
            using (PowerShell ps = PowerShell.Create())
            {
                ps.Runspace = _runspace;

                // Add script to delete the temporary file
                string deleteScript = @"
                        param($filePath)
                        Remove-Item -Path $filePath -Force
                        ";

                ps.AddScript(deleteScript);
                ps.AddParameter("filePath", Path.Combine(filePath, fileName) + "*");

                // Invoke the script to delete the file
                var results = ps.Invoke();
            }
        }

        public JobResult ImportPFXFile(string filePath, string privateKeyPassword, string cryptoProviderName)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;

                    if (cryptoProviderName == null)
                    {
                        string script = @"
                        param($pfxFilePath, $privateKeyPassword, $cspName)
                        $output = certutil -importpfx -p $privateKeyPassword $pfxFilePath 2>&1
                        $output
                        ";

                        ps.AddScript(script);
                        ps.AddParameter("pfxFilePath", filePath);
                        ps.AddParameter("privateKeyPassword", privateKeyPassword);
                    }
                    else
                    {
                        string script = @"
                        param($pfxFilePath, $privateKeyPassword, $cspName)
                        $output = certutil -importpfx -csp $cspName -p $privateKeyPassword $pfxFilePath 2>&1
                        $output
                        ";

                        ps.AddScript(script);
                        ps.AddParameter("pfxFilePath", filePath);
                        ps.AddParameter("privateKeyPassword", privateKeyPassword);
                        ps.AddParameter("cspName", cryptoProviderName);
                    }

                    // Invoke the script
                    var results = ps.Invoke();

                    // Check for errors in the output
                    bool isError = false;
                    foreach (var result in results)
                    {
                        string outputLine = result.ToString();
                        if (!string.IsNullOrEmpty(outputLine) && outputLine.Contains("Error"))
                        {
                            isError = true;
                            Console.WriteLine(outputLine); // Print the error message
                        }
                    }

                    if (isError)
                    {
                        throw new Exception("Error occurred while attempting to import the pfx file.");
                    }
                    else
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Success,
                            JobHistoryId = _jobNumber,
                            FailureMessage = ""
                        };
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in ClientPSCertStoreManager.ImportPFXFile(): {e.Message}");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobNumber,
                    FailureMessage = $"Error Occurred in ImportPFXFile {LogHandler.FlattenException(e)}"
                };
            }
        }

        public JobResult AddCertificate(string certificateContents, string privateKeyPassword, string storePath)
        {
            try
            {
                using var ps = PowerShell.Create();

                _logger.MethodEntry();

                ps.Runspace = _runspace;

                _logger.LogTrace($"Creating X509 Cert from: {certificateContents}");
                x509Cert = new X509Certificate2
                    (
                        Convert.FromBase64String(certificateContents),
                        privateKeyPassword,
                        X509KeyStorageFlags.MachineKeySet | 
                        X509KeyStorageFlags.PersistKeySet | 
                        X509KeyStorageFlags.Exportable
                    );

                // Add Certificate 
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
                _logger.LogDebug("InstallPfxToMachineStore Statement Added...");

                ps.AddCommand("InstallPfxToMachineStore")
                    .AddParameter("bytes", Convert.FromBase64String(certificateContents))
                    .AddParameter("password", privateKeyPassword)
                    .AddParameter("storeName", $@"\\{_runspace.ConnectionInfo.ComputerName}\{storePath}");
                
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
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error?.ErrorDetails.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = _jobNumber,
                            FailureMessage =
                                $"Site {storePath} on server {_runspace.ConnectionInfo.ComputerName}: {psError}"
                        };
                    }
                }

                _logger.LogTrace("Clearing Commands...");
                ps.Commands.Clear();
                _logger.LogTrace("Commands Cleared..");
                _logger.LogInformation($"Certificate was successfully added to cert store: {storePath}");
                
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = _jobNumber,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in ClientPSCertStoreManager.AddCertificate(): {e.Message}");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobNumber,
                    FailureMessage = $"Error Occurred in InstallCertificate {LogHandler.FlattenException(e)}"
                };
            }
        }

        public void RemoveCertificate(string thumbprint, string storePath)
        {
            using var ps = PowerShell.Create();

            _logger.MethodEntry();

            ps.Runspace = _runspace;

            var removeScript = $@"
                        $ErrorActionPreference = 'Stop'
                        $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{storePath}','LocalMachine')
                        $certStore.Open('MaxAllowed')
                        $certToRemove = $certStore.Certificates.Find(0,'{thumbprint}',$false)
                        if($certToRemove.Count -gt 0) {{
                            $certStore.Remove($certToRemove[0])
                        }}
                        $certStore.Close()
                        $certStore.Dispose()
                    ";

            ps.AddScript(removeScript);

            var _ = ps.Invoke();
            if (ps.HadErrors)
                throw new CertificateStoreException($"Error removing certificate in {storePath} store on {_runspace.ConnectionInfo.ComputerName}.");

        }
    }
}
