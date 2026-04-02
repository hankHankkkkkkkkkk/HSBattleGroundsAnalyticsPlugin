# Localization rules
- UI strings for the plugin must use resx resources.
- Do not hardcode Chinese or English strings in XAML or C#.
- Use key format: Page_Area_Meaning.
- Prefer minimal diffs.
- Do not change gameplay logic when implementing localization.
- Do not create custom translation tables for Hearthstone card/hero names in this task.
- Keep code compatible with current WPF project structure.

# Cleanup rules
- Prefer safety over aggressiveness when cleaning the repo.
- Never delete files if their purpose is unclear.
- Treat WPF/XAML indirect references as real usage.
- Do not run destructive git commands.
- Update .gitignore for build artifacts and local temp files.
- Keep diffs small and compilation-safe.
- For cleanup tasks, always produce a report before deleting anything.