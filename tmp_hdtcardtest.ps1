try {
  Add-Type -Path 'D:\Hank\HDT\HearthDb.dll'
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $configType=$asm.GetType('Hearthstone_Deck_Tracker.Config')
  $config=$configType.GetProperty('Instance',[Reflection.BindingFlags]'Public,Static').GetValue($null)
  $selectedField=$configType.GetField('SelectedLanguage',[Reflection.BindingFlags]'NonPublic,Instance')
  $cardDefsType=$asm.GetType('Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager')
  $loadLocale=$cardDefsType.GetMethod('LoadLocale',[Reflection.BindingFlags]'Public,Static')
  $cardType=$asm.GetType('Hearthstone_Deck_Tracker.Hearthstone.Card')
  $ctor=$cardType.GetConstructor(@([string]))

  $selectedField.SetValue($config,'en-US')
  $loadLocale.Invoke($null,@('enUS',$true)).GetAwaiter().GetResult()
  $cardEn=$ctor.Invoke(@('TB_BaconShop_HERO_11'))
  $nameEn=$cardType.GetProperty('LocalizedName').GetValue($cardEn)

  $selectedField.SetValue($config,'zh-CN')
  $loadLocale.Invoke($null,@('zhCN',$true)).GetAwaiter().GetResult()
  $cardZh=$ctor.Invoke(@('TB_BaconShop_HERO_11'))
  $nameZh=$cardType.GetProperty('LocalizedName').GetValue($cardZh)

  "EN=$nameEn`nZH=$nameZh" | Write-Output
} catch {
  $_ | Out-String | Write-Output
}
