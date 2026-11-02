# epub-lite-reader — agent instructions

Before editing Python, read .claude/rules/python-*.md

## Coverage

`pwsh tools/Measure-Coverage.ps1 -MinLine 100` is the gate. It runs the opt-in window suite, which opens real windows, so it needs an interactive desktop. Unreachable code gets deleted, not excluded.

## Release & Versioning

Before bumping any version, check the actual latest built/shipped artifact (local build output, GitHub releases, Partner Center) — never assume the next version from the repo file alone. Always add the new version's entry to the README/CHANGELOG 'What's new' section in the same commit as the bump.
