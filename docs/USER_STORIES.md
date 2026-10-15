---
codex: 1
project: MindAttic.Launcher
code: MCO
layer: stories
status: living
updated: 2026-10-14
---
<!-- workflow-db: this repo's stories and tickets live in the MindAttic.Workflow database -->

# MindAttic.Launcher — User Stories

This repo's stories and tickets (MCO-US-…, MCO-T…) live in the MindAttic.Workflow database
(`MindAttic.Workflow/data/workflow.db`), not in this file. Use the `workflow` CLI:

- Read: `workflow story list --repo MindAttic.Launcher`, `workflow ticket list --repo MindAttic.Launcher`, `workflow ticket show <ID>`
- Change: `workflow ticket new` / `close`, `workflow story status <ID> done|partial|planned`, `workflow ticket link <A> requires <B>`
- This file as it was, rebuilt from the database: `workflow db export --repo MindAttic.Launcher`
