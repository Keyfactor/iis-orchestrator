#Standard Step Names
# Step Name	        Purpose
# ValidateInput	    Validate required params and input data
# FindSite	        Checking if the IIS site exists
# CheckBinding	    Looking up existing bindings
# RemoveBinding	    Attempting to remove an old binding
# AddBinding	    Adding the new IIS binding
# LoadCertificate	Fetching or validating the SSL certificate
# CompareThumbprint	Checking if binding needs to be updated
# BindSSL	        Adding SSL cert to a binding
# ImportModules	    Importing IIS-related PowerShell modules
# AdminCheck	    Verifying the caller has local Administrator rights
# RemoveCertificate	Checking/removing a certificate if unused by any binding
# CatchAll	        Fallback for unexpected or generic errors

# Standard Error Codes
#Code	Status	Description
# 0	    Success	Operation completed successfully
# 100	Skipped	Binding already exists and is up-to-date
# 101	Warning	Binding exists but is invalid
# 200	Error	Site not found
# 201	Error	Failed to remove binding
# 202	Error	Failed to add binding
# 203	Error	Certificate not found
# 204	Error	Certificate already in use elsewhere
# 205	Error	Thumbprint mismatch
# 206	Error	WebAdministration module missing
# 207	Error	IISAdministration module missing
# 208	Skipped	Certificate still in use on another binding — removal skipped
# 209	Skipped	Certificate not found in store — nothing to remove
# 231	Error	Failed to load Microsoft.Web.Administration assembly
# 232	Error	Unexpected error while removing certificate from store
# 240	Error	Caller lacks local Administrator rights (shared check, Common module)
# 300	Error	Unknown or unhandled exception
# 400   Error   Invalid Ssl Flag bit combination

# Codes reserved for Add-KeyfactorCertificate / import path
# 501  Error   PFX payload could not be decoded or password was incorrect
# 510  Error   Requested CSP was not found on the target system
# 520  Error   certutil.exe returned a non-zero exit code
# 521  Error   certutil.exe could not be started
# 530  Error   Windows certificate store could not be opened for write

function New-KeyfactorResult {
    param(
        [ValidateSet("Success", "Warning", "Error", "Skipped")]
        [string]$Status,
        [int]$Code,
        [string]$Step,
        [string]$Message,
        [string]$ErrorMessage = "",
        [hashtable]$Details = @{}
    )

    return [PSCustomObject]@{
        Status       = $Status
        Code         = $Code
        Step         = $Step
        Message      = $Message
        ErrorMessage = $ErrorMessage
        Details      = $Details
    }
}