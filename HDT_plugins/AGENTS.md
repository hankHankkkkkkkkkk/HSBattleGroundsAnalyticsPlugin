# Localization rules
- UI strings for the plugin must use resx resources.
- Do not hardcode Chinese or English strings in XAML or C#.
- Use key format: Page_Area_Meaning.
- Prefer minimal diffs.
- Do not change gameplay logic when implementing localization.
- Do not create custom translation tables for Hearthstone card/hero names in this task.
- Keep code compatible with current WPF project structure.