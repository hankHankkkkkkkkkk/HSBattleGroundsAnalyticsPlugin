try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $asm.GetTypes() |
    Where-Object { $_.FullName -match 'Lang|lang|Locale|locale|Card|Db' } |
    Select-Object -First 300 -ExpandProperty FullName |
    Out-String |
    Write-Output
} catch {
  $_ | Out-String | Write-Output
}
