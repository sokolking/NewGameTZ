#!/bin/sh
cd "/Users/mac/Downloads/Hope" || exit 1
git status --porcelain > _git_status_out.txt 2>&1
git add -A
git status --porcelain >> _git_status_out.txt 2>&1
git diff --cached --stat >> _git_status_out.txt 2>&1
if git diff --cached --quiet && git diff --quiet; then
  echo "nothing_to_commit" >> _git_status_out.txt
else
  git commit -m "UI: LoginScene Figma layout; Esc settings tabs; MainMenuUI deep settings find

- LoginScene: AuthPanel, labels, inputs, toggles, Enter (1920x1080 ref)
- Esc settings: EscSettingsPanelSetupTool, GameAudioSettings, EscSettingsTabsController
- MainMenuUI: FindDeepChild for SettingsPanel under VideoPage" >> _git_status_out.txt 2>&1
fi
git push >> _git_status_out.txt 2>&1
echo "done" >> _git_status_out.txt
