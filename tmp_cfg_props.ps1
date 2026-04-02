try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Config')
  if($type -eq $null){ 'Config type not found'; return }
  $type.GetProperties([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Select-Object Name,@{n='Type';e={$_.PropertyType.FullName}} | Format-Table -AutoSize | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
