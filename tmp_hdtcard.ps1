try {
  $asm = [Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Hearthstone.Card')
  $type.GetConstructors([Reflection.BindingFlags]'Public,NonPublic,Instance') | ForEach-Object { $_.ToString() } | Out-String | Write-Output
  $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Where-Object { $_.Name -match 'From|Get|Locale|Name|CardId' } | Select-Object Name,@{n='Sig';e={$_.ToString()}} | Format-Table -AutoSize | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
