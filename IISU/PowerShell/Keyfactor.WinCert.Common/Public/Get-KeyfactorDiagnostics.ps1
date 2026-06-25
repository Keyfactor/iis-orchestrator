function Get-KeyfactorDiagnostics {
	$x = (whoami /groups /fo csv | ConvertFrom-Csv | ForEach-Object { $n = if ([string]::IsNullOrWhiteSpace($_.'Group Name')) { $_.SID } else { $_.'Group Name' }; "$n ($($_.SID))" }) -join ', '
    Write-Information $x
}