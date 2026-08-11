function Convert-DNSSubject {
    <#
    .SYNOPSIS
        Parses a Distinguished Name (DN) subject string and properly quotes RDN values containing escaped commas.
    
    .DESCRIPTION
        This function takes a DN subject string and parses the Relative Distinguished Name (RDN) components,
        adding proper quotes around values that contain escaped commas and escaping quotes for use in 
        PowerShell here-strings. Only RDN values with escaped commas get quoted.
    
    .PARAMETER Subject
        The DN subject string to parse (e.g., "CN=Keyfactor,O=Keyfactor\, Inc")
    
    .EXAMPLE
        Convert-DNSSubject -Subject "CN=Keyfactor,O=Keyfactor\, Inc"
        Returns: CN=Keyfactor,O=""Keyfactor, Inc""
    
    .EXAMPLE
        Convert-DNSSubject -Subject "CN=Test User,O=Company\, LLC,OU=IT Department\, Security"
        Returns: CN=Test User,O=""Company, LLC"",OU=""IT Department, Security""
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Subject
    )
    
    # Initialize variables
    $parsedComponents = @()
    $currentComponent = ""
    $i = 0
    
    # Convert string to character array for easier parsing
    $chars = $Subject.ToCharArray()
    
    while ($i -lt $chars.Length) {
        $char = $chars[$i]
        
        # Check if we hit a comma
        if ($char -eq ',') {
            # Look back to see if it's escaped
            $isEscaped = $false
            if ($i -gt 0 -and $chars[$i-1] -eq '\') {
                $isEscaped = $true
            }
            
            if ($isEscaped) {
                # This is an escaped comma, add it to current component
                $currentComponent += $char
            } else {
                # This is a separator comma, finish current component
                if ($currentComponent.Trim() -ne "") {
                    $parsedComponents += $currentComponent.Trim()
                    $currentComponent = ""
                }
            }
        } else {
            # Regular character, add to current component
            $currentComponent += $char
        }
        
        $i++
    }
    
    # Add the last component
    if ($currentComponent.Trim() -ne "") {
        $parsedComponents += $currentComponent.Trim()
    }
    
    # Process each component to add quotes where needed
    $processedComponents = @()
    
    foreach ($component in $parsedComponents) {
        # Split on first equals sign to get attribute and value
        $equalIndex = $component.IndexOf('=')
        if ($equalIndex -gt 0) {
            $attribute = $component.Substring(0, $equalIndex).Trim()
            $value = $component.Substring($equalIndex + 1).Trim()
            
            # Clean up escaped commas first
            $cleanValue = $value -replace '\\,', ','
            
            # Check if original value had escaped commas (needs quotes)
            if ($value -match '\\,') {
                # This RDN value had escaped commas, so wrap in doubled quotes and escape quotes
                $escapedValue = $cleanValue -replace '"', '""'
                $processedComponents += "$attribute=`"`"$escapedValue`"`""
            } else {
                # No escaped commas, keep as simple value but escape any existing quotes
                $escapedValue = $cleanValue -replace '"', '""'
                $processedComponents += "$attribute=$escapedValue"
            }
        } else {
            # Invalid component format, keep as is
            $processedComponents += $component
        }
    }
    
    # Join components back together (no outer quotes needed since it goes in PowerShell string)
    $subjectString = ($processedComponents -join ',')
    return $subjectString
}