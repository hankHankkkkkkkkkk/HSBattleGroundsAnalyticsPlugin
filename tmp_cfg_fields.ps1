try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $configType=$asm.GetType('Hearthstone_Deck_Tracker.Config')
  $configType.GetFields([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Select-Object Name, IsStatic, FieldType | Format-Table -AutoSize | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
