try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager')
  $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Select-Object Name,@{n='Sig';e={$_.ToString()}} | Format-Table -AutoSize | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
