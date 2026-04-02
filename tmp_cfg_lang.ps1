try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Config')
  $members = $type.GetMembers([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Where-Object { $_.Name -match 'Lang|lang|Locale|locale' }
  $members | Select-Object MemberType, Name | Format-Table -AutoSize | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
