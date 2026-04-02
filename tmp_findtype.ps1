try {
  $asm = [Reflection.Assembly]::LoadFrom('D:\Hank\HDT\HearthstoneDeckTracker.exe')
  $t = $asm.GetTypes() | Where-Object { $_.FullName -eq 'Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager' }
  if($t){ $t.AssemblyQualifiedName | Write-Output } else { 'not found' }
} catch {
  $_ | Out-String | Write-Output
}
