try {
  $asm=[Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $type=$asm.GetType('Hearthstone_Deck_Tracker.Config')
  $type.GetMembers([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') | Where-Object { $_.Name -match 'Language|Lang|Locale' } | ForEach-Object {
    if($_ -is [System.Reflection.FieldInfo]){ "FIELD static=$($_.IsStatic) $($_.FieldType.FullName) $($_.Name)" }
    elseif($_ -is [System.Reflection.MethodInfo]){ "METHOD static=$($_.IsStatic) $($_.ToString())" }
    else { "MEMBER $($_.MemberType) $($_.Name)" }
  } | Out-String | Write-Output
} catch { $_ | Out-String | Write-Output }
