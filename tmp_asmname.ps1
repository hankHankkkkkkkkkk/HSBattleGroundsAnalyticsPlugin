try {
  $asm = [Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $asm.GetName().Name | Write-Output
} catch {
  $_ | Out-String | Write-Output
}
