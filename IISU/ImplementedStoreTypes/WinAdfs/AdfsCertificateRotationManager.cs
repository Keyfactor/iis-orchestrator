// Copyright 2025 Keyfactor
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
// limitations under the License.using Keyfactor.Logging;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinAdfs
{
    public class AdfsCertificateRotationManager : IDisposable
    {
        private ILogger _logger;

        private PSHelper _primaryPsHelper;
        private string _protocol;
        private string _port;
        private bool _useSPN;
        private string _username;
        private string _password;
        private string _primaryNodeName;

        private List<string> _allNodes;
        private bool _disposed = false;

        public AdfsCertificateRotationManager(PSHelper primaryPsHelper, string protocol, string port, bool useSPN, string username, string password)
        {
            _logger = LogHandler.GetClassLogger<AdfsCertificateRotationManager>();

            _primaryPsHelper = primaryPsHelper ?? throw new ArgumentNullException(nameof(primaryPsHelper));
            _protocol = protocol;
            _port = port;
            _useSPN = useSPN;
            _username = username;
            _password = password;

            // Discover farm topology upon initialization
            DiscoverFarmTopology();
        }

        private void DiscoverFarmTopology()
        {
            _logger.MethodEntry();
            _logger.LogDebug("Discovering ADFS farm topology...");

            var results = _primaryPsHelper.InvokeFunction("Get-AdfsFarmNodeList");
            _allNodes = results.Select(r => r.ToString()).ToList();
            _primaryNodeName = _allNodes.FirstOrDefault();

            Console.WriteLine($"✓ Discovered {_allNodes.Count} node(s):");
            foreach (var node in _allNodes)
            {
                bool isPrimary = node.Equals(_primaryNodeName, StringComparison.OrdinalIgnoreCase);
                bool isLocal = _primaryPsHelper.IsLocalMachine && isPrimary;
                string indicator = isLocal ? " (LOCAL - current machine)" : isPrimary ? " (PRIMARY)" : " (SECONDARY)";
                _logger.LogTrace($"  - {node}{indicator}");
            }
            _logger.MethodExit();
        }

        public CertificateRotationResult RotateServiceCommunicationCertificate(string pfxFilePath, string pfxPassword)
        {
            _logger.MethodEntry();

            var result = new CertificateRotationResult();

            try
            {
                // Validate PFX file exists
                if (!File.Exists(pfxFilePath))
                {
                    throw new FileNotFoundException($"PFX file not found: {pfxFilePath}");
                }

                // Read PFX file into memory for remote transfers
                byte[] pfxBytes = null;
                if (!_primaryPsHelper.IsLocalMachine || _allNodes.Count > 1)
                {
                    pfxBytes = File.ReadAllBytes(pfxFilePath);
                    _logger.LogTrace($"✓ PFX file loaded ({pfxBytes.Length} bytes)\n");
                }

                // Step 1: Get service account name
                _logger.LogTrace("Retrieving ADFS service account name...");
                string serviceAccountName = GetServiceAccountName();
                _logger.LogTrace($"Service account name: {serviceAccountName}");

                // Step 2: Install certificate on all nodes
                _logger.LogTrace("Installing Certificate on All Nodes...");
                Dictionary<string, string> nodeThumbprints = new Dictionary<string, string>();

                foreach (string node in _allNodes)
                {
                    _logger.LogTrace($"Installing certificate on node: {node}...");

                    // Check if this is the local machine
                    bool isLocalNode = _primaryPsHelper.IsLocalMachine &&
                                      node.Equals(_primaryNodeName, StringComparison.OrdinalIgnoreCase);

                    string thumbprint;

                    if (isLocalNode)
                    {
                        // Use existing local connection
                        Console.WriteLine($"  Using local connection (application is running on this node)");
                        thumbprint = InstallCertificateOnLocalNode(pfxFilePath, pfxPassword, serviceAccountName);
                    }
                    else
                    {
                        // Create direct remote connection
                        thumbprint = InstallCertificateOnRemoteNode(node, pfxBytes, pfxPassword, serviceAccountName);
                    }

                    if (!string.IsNullOrEmpty(thumbprint))
                    {
                        nodeThumbprints[node] = thumbprint;
                        result.SuccessfulNodes.Add(node);
                    }
                    else
                    {
                        result.FailedNodes.Add(node);
                        result.Errors[node] = "Failed to install certificate";
                    }
                }

                // Check if all nodes succeeded
                if (result.FailedNodes.Count > 0)
                {
                    throw new Exception($"Certificate installation failed on {result.FailedNodes.Count} node(s)");
                }

                // Get the thumbprint (should be same on all nodes)
                string certificateThumbprint = nodeThumbprints.Values.First();
                result.Thumbprint = certificateThumbprint;

                // Step 3: Update ADFS farm settings on primary node
                _logger.LogTrace("Updating ADFS Farm Settings...");
                UpdateFarmCertificateSettings(certificateThumbprint);

                // Step 4: Restart ADFS service on all nodes
                _logger.LogTrace("Restarting ADFS Service...");
                RestartAdfsServicesSmartly();

                // Step 5: Verify installation
                _logger.LogTrace("Verifying Installation...");
                VerifyCertificateInstallationSmartly(certificateThumbprint);

                // Step 6: Clean up old certificates
                _logger.LogTrace("Cleaning Up Old Certificates...");
                CleanupOldCertificatesSmartly(certificateThumbprint);

                result.Success = true;
                result.Message = "Certificate rotation completed successfully";

                _logger.LogInformation($"New Certificate Thumbprint: {certificateThumbprint}");
                _logger.LogInformation($"Updated Nodes: {string.Join(", ", result.SuccessfulNodes)}");

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Certificate rotation failed: {ex.Message}";

                _logger.LogError($"Certificate rotation failed: {ex.Message}");
            }
            finally
            {
                _logger.MethodExit();
            }

            return result;
        }

        private string GetServiceAccountName()
        {
            try
            {
                _logger.MethodEntry();

                var results = _primaryPsHelper.InvokeFunction("Get-AdfsFarmProperties");

                if (results != null && results.Count > 0)
                {
                    string serviceAccount = results[0].Properties["ServiceAccountName"]?.Value?.ToString();

                    if (string.IsNullOrWhiteSpace(serviceAccount))
                    {
                        _logger.LogWarning("⚠ Warning: Service account name not available from ADFS properties");
                        _logger.LogWarning("  ADFS may be using gMSA or built-in account");
                        return null;
                    }

                    return serviceAccount;
                }

                _logger.LogWarning("⚠ Warning: Could not retrieve ADFS properties");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠ Warning: Error retrieving service account: {ex.Message}");
                return null;
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private string InstallCertificateOnLocalNode(string pfxFilePath, string pfxPassword, string serviceAccountName)
        {
            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Installing certificate on local node...");
                _logger.LogTrace($"  Using file path: {pfxFilePath}");

                var parameters = new Dictionary<string, object>
                {
                    { "PfxFilePath", pfxFilePath },
                    { "PfxPasswordText", pfxPassword }
                };

                var results = _primaryPsHelper.InvokeFunction("Install-AdfsCertificateOnNode", parameters);

                string thumbprint = null;
                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                        thumbprint = results[0].Properties["Thumbprint"]?.Value?.ToString();
                        string subject = results[0].Properties["Subject"]?.Value?.ToString();

                        _logger.LogTrace($"  ✓ Certificate installed successfully");
                        _logger.LogTrace($"    Subject: {subject}");

                        // Grant permissions using local connection
                        GrantCertificatePermissionsLocal(thumbprint, serviceAccountName);
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        _logger.LogError($"  ✗ Installation failed: {errorMsg}");
                    }
                }

                return thumbprint;
            }
            catch (Exception ex)
            {
                _logger.LogError($"  ✗ Error: {ex.Message}");
                return null;
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private string InstallCertificateOnRemoteNode(string nodeName, byte[] pfxBytes, string pfxPassword, string serviceAccountName)
        {
            _logger.MethodEntry();

            PSHelper nodeHelper = null;

            try
            {
                _logger.LogTrace($"  Establishing direct connection to {nodeName}...");

                // Create a NEW direct PSHelper connection to this specific node
                nodeHelper = new PSHelper(
                    _protocol,
                    _port,
                    _useSPN,
                    nodeName,
                    _username,
                    _password
                );

                nodeHelper.Initialize();

                _logger.LogTrace($"  ✓ Connected to {nodeName}");

                // Create temporary PFX file on the remote node
                string remoteTempPath = CreateTempPfxOnNode(nodeHelper, pfxBytes);
                _logger.LogTrace($"  ✓ PFX transferred to {nodeName}: {remoteTempPath}");

                // Install certificate
                _logger.LogTrace($"  Installing certificate...");
                var parameters = new Dictionary<string, object>
                {
                    { "PfxFilePath", remoteTempPath },
                    { "PfxPasswordText", pfxPassword }
                };

                var results = nodeHelper.InvokeFunction("Install-AdfsCertificateOnNode", parameters);

                string thumbprint = null;
                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                        thumbprint = results[0].Properties["Thumbprint"]?.Value?.ToString();
                        string subject = results[0].Properties["Subject"]?.Value?.ToString();

                        _logger.LogTrace($"  ✓ Certificate installed successfully");
                        _logger.LogTrace($"    Thumbprint: {thumbprint}");
                        _logger.LogTrace($"    Subject: {subject}");

                        // Grant permissions
                        GrantCertificatePermissionsOnNode(nodeHelper, thumbprint, serviceAccountName);

                        // Clean up temp file
                        CleanupTempFileOnNode(nodeHelper, remoteTempPath);
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        _logger.LogError($"  ✗ Installation failed: {errorMsg}");
                    }
                }

                return thumbprint;
            }
            catch (Exception ex)
            {
                _logger.LogError($"  ✗ Error: {ex.Message}");
                return null;
            }
            finally
            {
                // Clean up the direct connection
                if (nodeHelper != null)
                {
                    try
                    {
                        nodeHelper.Terminate();
                        nodeHelper.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"  Warning: Error closing connection to {nodeName}: {ex.Message}");
                    }
                }

                _logger.MethodExit();
            }
        }

        private void GrantCertificatePermissionsLocal(string thumbprint, string serviceAccountName)
        {
            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Granting permissions to service account...");

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                // Only add service account if not null/empty
                if (!string.IsNullOrWhiteSpace(serviceAccountName))
                {
                    parameters.Add("ServiceAccountName", serviceAccountName);
                }

                var results = _primaryPsHelper.InvokeFunction("Grant-AdfsCertificatePermissions", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;
                    var skipped = results[0].Properties["Skipped"]?.Value;
                    var alreadyGranted = results[0].Properties["AlreadyGranted"]?.Value;
                    var message = results[0].Properties["Message"]?.Value?.ToString();

                    if (success != null && (bool)success)
                    {
                        if (skipped != null && (bool)skipped)
                        {
                            _logger.LogWarning($"  ⚠ Permissions skipped: {message}");
                        }
                        else if (alreadyGranted != null && (bool)alreadyGranted)
                        {
                            _logger.LogTrace($"  ✓ Permissions already granted");
                        }
                        else
                        {
                            _logger.LogTrace($"  ✓ Permissions granted");
                        }
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        _logger.LogWarning($"  ⚠ Warning: Could not grant permissions - {errorMsg}");
                        _logger.LogWarning($"  Certificate may still work if service account has existing access");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ⚠ Warning: Error granting permissions - {ex.Message}");
                _logger.LogWarning($"  Certificate may still work if ADFS runs as SYSTEM or has existing access");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private void GrantCertificatePermissionsOnNode(PSHelper nodeHelper, string thumbprint, string serviceAccountName)
        {
            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Granting permissions to service account...");

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                // Only add service account if not null/empty
                if (!string.IsNullOrWhiteSpace(serviceAccountName))
                {
                    parameters.Add("ServiceAccountName", serviceAccountName);
                }

                var results = nodeHelper.InvokeFunction("Grant-AdfsCertificatePermissions", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;
                    var skipped = results[0].Properties["Skipped"]?.Value;
                    var alreadyGranted = results[0].Properties["AlreadyGranted"]?.Value;
                    var message = results[0].Properties["Message"]?.Value?.ToString();

                    if (success != null && (bool)success)
                    {
                        if (skipped != null && (bool)skipped)
                        {
                            _logger.LogWarning($"  ⚠ Permissions skipped: {message}");
                        }
                        else if (alreadyGranted != null && (bool)alreadyGranted)
                        {
                            _logger.LogTrace($"  ✓ Permissions already granted");
                        }
                        else
                        {
                            _logger.LogTrace($"  ✓ Permissions granted");
                        }
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        _logger.LogWarning($"  ⚠ Warning: Could not grant permissions - {errorMsg}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ⚠ Warning: Error granting permissions - {ex.Message}");
                _logger.LogWarning($"  Certificate may still work depending on service account configuration");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private void UpdateFarmCertificateSettings(string thumbprint)
        {
            _logger.MethodEntry();

            try
            {
                _logger.LogInformation("Updating farm settings on primary node...");

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                var results = _primaryPsHelper.InvokeFunction("Update-AdfsFarmCertificateSettings", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                        _logger.LogInformation("ADFS farm certificate settings updated");
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        throw new Exception($"Failed to update farm settings: {errorMsg}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"    Error updating farm settings: {ex.Message}");
                throw;
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        public static void UpdateFarmCertificateSettings(string thumbprint, PSHelper psHelper)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                var results = psHelper.InvokeFunction("Update-AdfsFarmCertificateSettings", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                    }
                    else
                    {
                        string errorMsg = results[0].Properties["ErrorMessage"]?.Value?.ToString();
                        throw new Exception($"Failed to update farm settings: {errorMsg}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private void RestartAdfsServicesSmartly()
        {
            foreach (string nodeName in _allNodes)
            {
                bool isLocalNode = _primaryPsHelper.IsLocalMachine &&
                                  nodeName.Equals(_primaryNodeName, StringComparison.OrdinalIgnoreCase);

                if (isLocalNode)
                {
                    // Use existing local connection
                    RestartAdfsServiceLocal(nodeName);
                }
                else
                {
                    // Create remote connection
                    RestartAdfsServiceRemote(nodeName);
                }
            }
        }

        private void RestartAdfsServiceLocal(string nodeName)
        {
            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Restarting ADFS on {nodeName} (local)...");

                var results = _primaryPsHelper.InvokeFunction("Restart-AdfsServiceOnNode");

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                        _logger.LogTrace($"  ✓ ADFS service restarted on {nodeName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ✗ Error restarting {nodeName}: {ex.Message}");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private void RestartAdfsServiceRemote(string nodeName)
        {
            PSHelper nodeHelper = null;

            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Restarting ADFS on {nodeName}...");

                // Create direct connection
                nodeHelper = new PSHelper(_protocol, _port, _useSPN, nodeName, _username, _password);
                nodeHelper.Initialize();

                // Restart service
                var results = nodeHelper.InvokeFunction("Restart-AdfsServiceOnNode");

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;

                    if (success != null && (bool)success)
                    {
                        _logger.LogTrace($"  ✓ ADFS service restarted on {nodeName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ✗ Error restarting {nodeName}: {ex.Message}");
            }
            finally
            {
                if (nodeHelper != null)
                {
                    try
                    {
                        nodeHelper.Terminate();
                        nodeHelper.Dispose();
                    }
                    catch { }
                }

                _logger.MethodExit();
            }
        }

        private void VerifyCertificateInstallationSmartly(string thumbprint)
        {
            _logger.MethodEntry();

            _logger.LogTrace("Verifying certificate installation on all nodes...\n");

            foreach (string nodeName in _allNodes)
            {
                bool isLocalNode = _primaryPsHelper.IsLocalMachine &&
                                  nodeName.Equals(_primaryNodeName, StringComparison.OrdinalIgnoreCase);

                if (isLocalNode)
                {
                    VerifyCertificateLocal(nodeName, thumbprint);
                }
                else
                {
                    VerifyCertificateRemote(nodeName, thumbprint);
                }
            }
            _logger.MethodExit();
        }

        private void VerifyCertificateLocal(string nodeName, string thumbprint)
        {
            try
            {
                _logger.MethodEntry();

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                var results = _primaryPsHelper.InvokeFunction("Test-AdfsCertificateInstalled", parameters);

                if (results != null && results.Count > 0)
                {
                    var isInstalled = results[0].Properties["IsInstalled"]?.Value;
                    var hasPrivateKey = results[0].Properties["HasPrivateKey"]?.Value;

                    if (isInstalled != null && (bool)isInstalled)
                    {
                        _logger.LogTrace($"  ✓ {nodeName} (local): Certificate verified");
                        _logger.LogTrace($"    Has Private Key: {hasPrivateKey}");
                    }
                    else
                    {
                        _logger.LogTrace($"  ✗ {nodeName}: Certificate NOT found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ✗ {nodeName}: Verification failed - {ex.Message}");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private void VerifyCertificateRemote(string nodeName, string thumbprint)
        {
            PSHelper nodeHelper = null;

            try
            {
                _logger.MethodEntry();

                // Create direct connection
                nodeHelper = new PSHelper(_protocol, _port, _useSPN, nodeName, _username, _password);
                nodeHelper.Initialize();

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateThumbprint", thumbprint }
                };

                var results = nodeHelper.InvokeFunction("Test-AdfsCertificateInstalled", parameters);

                if (results != null && results.Count > 0)
                {
                    var isInstalled = results[0].Properties["IsInstalled"]?.Value;
                    var hasPrivateKey = results[0].Properties["HasPrivateKey"]?.Value;

                    if (isInstalled != null && (bool)isInstalled)
                    {
                        _logger.LogTrace($"  ✓ {nodeName}: Certificate verified");
                        _logger.LogTrace($"    Has Private Key: {hasPrivateKey}");
                    }
                    else
                    {
                        _logger.LogTrace($"  ✗ {nodeName}: Certificate NOT found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ✗ {nodeName}: Verification failed - {ex.Message}");
            }
            finally
            {
                if (nodeHelper != null)
                {
                    try
                    {
                        nodeHelper.Terminate();
                        nodeHelper.Dispose();
                    }
                    catch { }
                }

                _logger.MethodExit();
            }
        }

        private void CleanupOldCertificatesSmartly(string newThumbprint)
        {
            _logger.MethodEntry();

            // Use primary connection to get cert details
            var certParams = new Dictionary<string, object>
            {
                { "CertificateThumbprint", newThumbprint }
            };

            var certResults = _primaryPsHelper.InvokeFunction("Test-AdfsCertificateInstalled", certParams);

            if (certResults == null || certResults.Count == 0)
            {
                _logger.LogTrace("  Could not retrieve certificate details for cleanup");
                return;
            }

            string subject = certResults[0].Properties["Subject"]?.Value?.ToString();
            DateTime notAfter = (DateTime)certResults[0].Properties["NotAfter"]?.Value;

            _logger.LogTrace($"Removing old certificates with subject: {subject}\n");

            foreach (string nodeName in _allNodes)
            {
                bool isLocalNode = _primaryPsHelper.IsLocalMachine &&
                                  nodeName.Equals(_primaryNodeName, StringComparison.OrdinalIgnoreCase);

                if (isLocalNode)
                {
                    CleanupOldCertificatesLocal(nodeName, subject, notAfter);
                }
                else
                {
                    CleanupOldCertificatesRemote(nodeName, subject, notAfter);
                }
            }

            _logger.MethodExit();
        }

        private void CleanupOldCertificatesLocal(string nodeName, string subject, DateTime notAfter)
        {
            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Cleaning up {nodeName} (local)...");

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateSubject", subject },
                    { "NewCertificateNotAfter", notAfter }
                };

                var results = _primaryPsHelper.InvokeFunction("Remove-OldAdfsCertificate", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;
                    var removedCount = results[0].Properties["RemovedCount"]?.Value;

                    if (success != null && (bool)success)
                    {
                        _logger.LogTrace($"  ✓ Removed {removedCount} old certificate(s) from {nodeName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ⚠ Cleanup warning for {nodeName}: {ex.Message}");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private void CleanupOldCertificatesRemote(string nodeName, string subject, DateTime notAfter)
        {
            PSHelper nodeHelper = null;

            try
            {
                _logger.MethodEntry();

                _logger.LogTrace($"  Cleaning up {nodeName}...");

                // Create direct connection
                nodeHelper = new PSHelper(_protocol, _port, _useSPN, nodeName, _username, _password);
                nodeHelper.Initialize();

                var parameters = new Dictionary<string, object>
                {
                    { "CertificateSubject", subject },
                    { "NewCertificateNotAfter", notAfter }
                };

                var results = nodeHelper.InvokeFunction("Remove-OldAdfsCertificate", parameters);

                if (results != null && results.Count > 0)
                {
                    var success = results[0].Properties["Success"]?.Value;
                    var removedCount = results[0].Properties["RemovedCount"]?.Value;

                    if (success != null && (bool)success)
                    {
                        _logger.LogTrace($"  ✓ Removed {removedCount} old certificate(s) from {nodeName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  ⚠ Cleanup warning for {nodeName}: {ex.Message}");
            }
            finally
            {
                if (nodeHelper != null)
                {
                    try
                    {
                        nodeHelper.Terminate();
                        nodeHelper.Dispose();
                    }
                    catch { }
                }

                _logger.MethodExit();
            }
        }

        private string CreateTempPfxOnNode(PSHelper nodeHelper, byte[] pfxBytes)
        {
            // Convert bytes to Base64 for transfer
            string base64Content = Convert.ToBase64String(pfxBytes);

            string script = $@"
            $tempPath = [System.IO.Path]::Combine($env:TEMP, 'adfs_cert_' + [System.Guid]::NewGuid().ToString() + '.pfx')
            $bytes = [System.Convert]::FromBase64String('{base64Content}')
            [System.IO.File]::WriteAllBytes($tempPath, $bytes)
            return $tempPath
        ";

            var results = nodeHelper.ExecutePowerShell(script, isScript: true);

            if (results != null && results.Count > 0)
            {
                return results[0].ToString();
            }

            throw new Exception("Failed to create temporary PFX file on remote node");
        }

        private void CleanupTempFileOnNode(PSHelper nodeHelper, string remotePath)
        {
            try
            {
                _logger.MethodEntry();

                var parameters = new Dictionary<string, object>
                {
                    { "FilePath", remotePath }
                };

                nodeHelper.InvokeFunction("Remove-TempFileOnNode", parameters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  Warning: Could not remove temp file: {ex.Message}");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _primaryPsHelper = null;
                }
                _disposed = true;
            }
        }
    }
}
