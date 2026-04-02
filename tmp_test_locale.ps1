try {
  Add-Type -Path 'D:\Hank\HDT\HearthDb.dll'
  $asm = [Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $cardDefsType = $asm.GetType('Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager')
  if($cardDefsType -eq $null) { 'CardDefsManager not found'; exit }
  $loadLocale = $cardDefsType.GetMethod('LoadLocale', [Reflection.BindingFlags]'Public,Static')
  $cardId = 'TB_BaconShop_HERO_11'
  $before = [HearthDb.Cards]::All[$cardId].Name
  $task = $loadLocale.Invoke($null, @('zhCN', $true))
  $task.GetAwaiter().GetResult()
  $afterZh = [HearthDb.Cards]::All[$cardId].Name
  $task2 = $loadLocale.Invoke($null, @('enUS', $true))
  $task2.GetAwaiter().GetResult()
  $afterEn = [HearthDb.Cards]::All[$cardId].Name
  "Before=$before`nZh=$afterZh`nEn=$afterEn" | Write-Output
} catch {
  $_ | Out-String | Write-Output
}
