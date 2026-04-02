try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $cfg = $asm.GetTypes() | Where-Object { $_.FullName -match 'Config$|Config\.' -or $_.Name -eq 'Config' } | Select-Object -ExpandProperty FullName
  $cfg | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
