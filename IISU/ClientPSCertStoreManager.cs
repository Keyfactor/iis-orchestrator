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
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq.Expressions;
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
            _logger.LogTrace("Entering CreatePFXFile");
            if (!string.IsNullOrEmpty(privateKeyPassword)) { _logger.LogTrace("privateKeyPassword was present"); } 
            else _logger.LogTrace("No privateKeyPassword Presented");

            try
            {
                // Create the x509 certificate
                x509Cert = new X509Certificate2
                    (
                        Convert.FromBase64String(certificateContents),
                        privateKeyPassword,
                        X509KeyStorageFlags.MachineKeySet |
                        X509KeyStorageFlags.PersistKeySet |
                        X509KeyStorageFlags.Exportable
                    );

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
                    _logger.LogTrace($"Results after creating PFX File: {results[0].ToString()}");
                    return results[0].ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
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
                _logger.LogTrace("Entering ImportPFX");

                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;

                    if (cryptoProviderName == null)
                    {
                        //string script = @"
                        //param($pfxFilePath, $privateKeyPassword)
                        //$output = certutil -importpfx -p $privateKeyPassword $pfxFilePath 2>&1
                        //$c = $LASTEXITCODE 2>&1
                        //$output
                        //";

                        string script = @"
                        param($pfxFilePath, $privateKeyPassword)
                        $output = certutil -importpfx -p $privateKeyPassword $pfxFilePath 2>&1
                        $exit_message = ""LASTEXITCODE:$($LASTEXITCODE)""
                        $stuff = certutil -dump

                        if ($stuff.GetType().Name -eq ""String"")
                        {
                            $stuff = @($stuff, $exit_message)
                        }
                        else
                        {
                            $stuff += $exit_message
                        }

                        $output
                        $stuff
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
                        $exit_message = ""LASTEXITCODE:$($LASTEXITCODE)""
                        $stuff = certutil -dump

                        if ($stuff.GetType().Name -eq ""String"")
                        {
                            $stuff = @($stuff, $exit_message)
                        }
                        else
                        {
                            $stuff += $exit_message
                        }

                        $output
                        $stuff
                        ";

                        ps.AddScript(script);
                        ps.AddParameter("pfxFilePath", filePath);
                        ps.AddParameter("privateKeyPassword", privateKeyPassword);
                        ps.AddParameter("cspName", cryptoProviderName);
                    }

                    // Invoke the script
                    _logger.LogTrace("Attempting to import the PFX");
                    var results = ps.Invoke();

                    // Get the last exist code returned from the script
                    // This statement is in a try/catch block because PSVariable.GetValue() is not a valid method on a remote PS Session and throws an exception.
                    // Due to security reasons and Windows architecture, retreiving values from a remote system is not supported.
                    int lastExitCode = 0;
                    try
                    {
                        lastExitCode = GetLastExitCode(results[^1].ToString());
                        _logger.LogTrace($"Last exit code: {lastExitCode}");
                    }
                    catch (Exception)
                    {
                        _logger.LogTrace("Unable to get the last exit code.");
                    }
                    

                    bool isError = false;
                    if (lastExitCode != 0)
                    {
                        isError = true;
                        string outputMsg = "";

                        foreach (var result in results)
                        {
                            string outputLine = result.ToString();
                            if (!string.IsNullOrEmpty(outputLine))
                            {
                                outputMsg += "\n" + outputLine;
                            }
                        }
                        _logger.LogError(outputMsg);
                    }
                    else
                    {
                        // Check for errors in the output
                        foreach (var result in results)
                        {
                            string outputLine = result.ToString();

                            _logger.LogTrace(outputLine);

                            if (!string.IsNullOrEmpty(outputLine) && outputLine.Contains("Error") || outputLine.Contains("permissions are needed"))
                            {
                                isError = true;
                                _logger.LogError(outputLine);
                            }
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

        private int GetLastExitCode(string result)
        {
            // Split the string by colon
            string[] parts = result.Split(':');

            // Ensure the split result has the expected parts
            if (parts.Length == 2 && parts[0] == "LASTEXITCODE")
            {
                // Parse the second part into an integer
                if (int.TryParse(parts[1], out int lastExitCode))
                {
                    return lastExitCode;
                }
                else
                {
                    throw new Exception("Failed to parse the LASTEXITCODE value.");
                }
            }
            else
            {
                throw new Exception("The last element does not contain the expected format.");
            }
        }

        public void RemoveCertificate(string thumbprint, string storePath)
        {
            using var ps = PowerShell.Create();

            _logger.MethodEntry();

            ps.Runspace = _runspace;

            // Open with value of 5 means:  Open existing only (4) + Open ReadWrite (1)
            var removeScript = $@"
                        $ErrorActionPreference = 'Stop'
                        $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{storePath}','LocalMachine')
                        $certStore.Open(5)
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
