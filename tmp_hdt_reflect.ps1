try {
  [Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe').GetManifestResourceNames() |
    Where-Object { $_ -match 'locale|local|card|xml|json|Lang|lang' } |
    Out-String |
    Write-Output
} catch {
  $_ | Out-String | Write-Output
}
