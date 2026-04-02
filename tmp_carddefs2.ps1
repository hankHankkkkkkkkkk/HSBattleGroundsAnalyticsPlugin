try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager')
  $type.GetMembers([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Where-Object { $_.Name -match 'LoadLocale|Locale' } | ForEach-Object {
    if($_ -is [System.Reflection.MethodInfo]){ "METHOD static=$($_.IsStatic) $($_.ToString())" }
    elseif($_ -is [System.Reflection.PropertyInfo]){ $accessor = if($_.GetMethod){ $_.GetMethod } else { $_.SetMethod }; "PROP static=$($accessor.IsStatic) $($_.PropertyType.FullName) $($_.Name)" }
    else { "MEMBER $($_.MemberType) $($_.Name)" }
  } | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
