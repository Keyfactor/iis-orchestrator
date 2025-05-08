using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Scripts
{
    public class PowerShellScripts
    {
        public const string UpdateIISBindings = @"
                param (
                    $SiteName,        # The name of the IIS site
                    $IPAddress,       # The IP Address for the binding
                    $Port,            # The port number for the binding
                    $Hostname,        # Hostname for the binding (if any)
                    $Protocol,        # Protocol (e.g., HTTP, HTTPS)
                    $Thumbprint,      # Certificate thumbprint for HTTPS bindings
                    $StoreName,       # Certificate store location (e.g., ""My"" for personal certs)
                    $SslFlags         # SSL flags (if any)
                )

                # Set Execution Policy (optional, depending on your environment)
                Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force

                ##  Check if the IISAdministration module is available
                #$module = Get-Module -Name IISAdministration -ListAvailable

                #if (-not $module) {
                # throw ""The IISAdministration module is not installed on this system.""
                #}

                # Check if the IISAdministration module is already loaded
                if (-not (Get-Module -Name IISAdministration)) {
                    try {
                        # Attempt to import the IISAdministration module
                        Import-Module IISAdministration -ErrorAction Stop
                    }
                    catch {
                        throw ""Failed to load the IISAdministration module. Ensure it is installed and available.""
                    }
                }

                # Retrieve the existing binding information
                $myBinding = ""${IPAddress}:${Port}:${Hostname}""
                Write-Host ""myBinding: "" $myBinding

                $siteBindings = Get-IISSiteBinding -Name $SiteName
                $existingBinding = $siteBindings | Where-Object { $_.bindingInformation -eq $myBinding -and $_.protocol -eq $Protocol }
    
                Write-Host ""Binding:"" $existingBinding

                if ($null -ne $existingBinding) {
                    # Remove the existing binding
                    Remove-IISSiteBinding -Name $SiteName -BindingInformation $existingBinding.BindingInformation -Protocol $existingBinding.Protocol -Confirm:$false
        
                    Write-Host ""Removed existing binding: $($existingBinding.BindingInformation)""
                }
        
                # Create the new binding with modified properties
                $newBindingInfo = ""${IPAddress}:${Port}:${Hostname}""
        
                try
                {
                    New-IISSiteBinding -Name $SiteName `
                        -BindingInformation $newBindingInfo `
                        -Protocol $Protocol `
                        -CertificateThumbprint $Thumbprint `
                        -CertStoreLocation $StoreName `
                        -SslFlag $SslFlags

                    Write-Host ""New binding added: $newBindingInfo""
                }
                catch {
                    throw $_
                }";

    }
}
