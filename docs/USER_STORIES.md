---
codex: 1
project: MindAttic.Launcher
code: M.LNCH
layer: stories
status: living
updated: 2026-10-14
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

# MindAttic.Launcher — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test.
> Personas: **Dev** (the developer running the workspace).

## Epic A — Launch agents in tabs
- **M.LNCH-US-A1 ✅** As a Dev, I can run `mindattic host --name <Project> [--provider <Key>]` to root
  an agent in the right repo; without `--provider` it resolves to the first-listed provider (Claude),
  so I get a working session in one step even from a script. *Given a roster with the project; When I
  host it with no explicit provider; Then the project's path and the first-listed provider are
  resolved.* *(verified by `Defaults_are_returned_when_AgentProviders_is_empty`,
  `Current_is_the_first_configured_provider`.)*
- **M.LNCH-US-A2 🟡** As a Dev, when I reference a provider that doesn't exist, the operation fails
  loudly rather than launching a default, so a typo never starts the wrong agent. *Given an unknown
  provider key; When resolving it; Then the caller gets nothing back to fall through to a loud
  error.* *(registry rejection verified by `SetModel_unknown_key_throws`; the `HostAgentCommand`
  `--provider` exit-code path itself — [M.LNCH-LAW-1](BIBLE.md#M.LNCH-LAW-1) — is not yet covered by an
  automated test.)*
- **M.LNCH-US-A3 🟡** As a Dev, opening a project tab asks fresh which agent CLI to launch it with —
  Claude first, then Codex, Gemini, Kimi — every single time, so nothing about "which CLI for this
  project" is ever saved and I can open the same project with a different CLI on a whim. *Given the
  Open Project Tab menu; When I pick a project; Then a picker over all configured providers appears,
  ordered Claude/Codex/Gemini/Kimi, and the launch uses whichever one I pick without persisting it.*
  *(the ordering contract is verified by `Defaults_order_is_Claude_Codex_Gemini_Kimi`; the menu-level
  picker itself — `OpenProjectMenu.PickProvider` — is exercised only interactively, no UI test
  harness exists yet.)*
- **M.LNCH-US-A4 ✅** As a Dev, a provider `RunCommand` with quoted/spaced arguments is split into argv
  correctly before exec. *Given a quoted command line; When split; Then quotes and escaped quotes
  are preserved.* *(verified by `Preserves_quoted_arg_with_spaces`,
  `Preserves_escaped_quote_inside_quoted_arg`, `Splits_unquoted_args`,
  `Empty_input_returns_empty_array`.)*
- **M.LNCH-US-A5 🟡** As a Dev, I can open a project tab from the interactive menu (title + color +
  scheme via `wt`) after picking the agent CLI for this launch. *WT command-line composition is
  covered, but the menu-driven open flow itself is exercised only interactively.* *(launcher partly
  verified by `WindowsTerminalLauncherTests`; menu path unverified.)*
- **M.LNCH-US-A6 ✅** As a Dev, I can set the model each agent CLI runs with — or revert to the CLI's
  own default — from the Settings screen, so I can switch Claude/Codex models without retyping the
  whole `RunCommand`. *Given a provider's RunCommand; When I set a model; Then the `--model` token is
  rewritten in place (or appended/removed) and persisted, leaving the static Defaults untouched.*
  *(token rewrite verified by `Set_rewrites_existing_flag_in_place`,
  `Set_appends_flag_when_absent`, `Set_blank_removes_flag_without_leaving_double_space`,
  `Set_then_Get_round_trips`; persistence by `SetModel_appends_flag_and_persists`,
  `SetModel_blank_clears_the_flag`, `SetModel_unknown_key_throws`,
  `SetModel_materializes_defaults_when_none_configured`.)*

## Epic B — Tab title & remote control
- **M.LNCH-US-B1 ✅** As a Dev, each host tab shows whether the agent is busy, so I can read the tab
  strip at a glance. *Given a Claude/Codex working footer **or a background shell the agent spawned
  still running**; When the pinner peeks the buffer; Then it reports busy and uses the play glyph.*
  *(verified by `LooksBusy_matches_the_Claude_working_footer`,
  `LooksBusy_matches_the_Codex_working_footer`, `LooksBusy_is_false_for_an_idle_prompt`,
  `HasBackgroundShell_matches_a_running_background_shell`,
  `HasBackgroundShell_is_false_for_an_idle_prompt_with_no_shells`,
  `Compose_uses_the_play_glyph_when_busy`.)*
- **M.LNCH-US-B2 ✅** As a Dev, I can broadcast text (e.g. `/remote-control`) to every running host tab
  for a provider via per-tab named pipes. *Given several host pipes; When I broadcast; Then only
  matching-provider pipes receive the payload.* *(verified by
  `Filters_pipes_by_provider_prefix_and_writes_payload`,
  `Reports_zero_delivered_when_no_pipes_match`.)*

## Epic C — Commit & push
- **M.LNCH-US-C1 ✅** As a Dev, I can commit + push one or all projects, with an auto-generated message
  from `git status --porcelain` when I give none. *Given a dirty tree; When I read status; Then it
  parses adds/mods/deletes/renames/untracked and composes a capped summary.* *(verified by
  `Status_codes_classified_correctly`, `Rename_picks_up_destination_filename`,
  `Untracked_files_count_as_added`, `Empty_porcelain_is_clean`, `Quoted_paths_are_unwrapped`,
  `AutoMessage_groups_by_action`, `AutoMessage_truncates_to_summary_when_over_limit`,
  `Short_format_uses_compact_counts`.)*
- **M.LNCH-US-C2 ✅** As a Dev, an unreadable repo (missing path / not a repo / timeout) is treated as
  "unknown", never silently "clean", so I never push past a broken status. *Given an error status;
  When summarized; Then it is not clean and surfaces the error.* *(verified by
  `Status_with_error_is_not_clean_and_short_returns_error`,
  `Status_for_missing_directory_returns_error_state`.)*

## Epic D — Backup
- **M.LNCH-US-D1 ✅** As a Dev, backup writes to a dated folder that never collides, walking
  `<date>`, `<date>_a`, …`_z`. *Given today's folder taken; When resolving; Then it appends the next
  free letter and throws only when all 27 slots are used.* *(verified by
  `ResolveTargetFolder_uses_dated_folder_when_unused`,
  `ResolveTargetFolder_appends_underscore_a_on_collision`,
  `ResolveTargetFolder_walks_to_first_available_letter`,
  `ResolveTargetFolder_throws_when_all_27_slots_taken`.)*
- **M.LNCH-US-D2 ✅** As a Dev, backup is reported failed if it was cancelled even when the tool's exit
  code looked successful. *Given a success code but cancelled=true; When computing OK; Then it
  reports failure.* *(verified by `ComputeOk_reports_failure_when_cancelled_even_on_a_success_code`.)*
- **M.LNCH-US-D3 ✅** As a Dev, each project database gets a real full backup, namespaced by server so
  the same DB on two instances doesn't collide, with illegal path chars scrubbed and identifiers
  escaped. *(verified by `ResolveBackupFilePath_places_bak_under_databases_subfolder`,
  `ResolveBackupFilePath_namespaces_by_server_so_same_db_on_two_instances_does_not_collide`,
  `ResolveBackupFilePath_scrubs_path_illegal_characters_in_server_and_database`,
  `BuildBackupSql_is_a_full_copy_only_checksummed_backup_with_escaped_identifiers` + the wider
  `SqlBackupServiceTests`.)*
- **M.LNCH-US-D4 ✅** As a Dev, the robocopy exclude lists match the original PowerShell launcher so the
  snapshot omits the same noise (bin/obj/etc.). *(verified by `Exclude_lists_match_original_PS_launcher`.)*
- **M.LNCH-US-D5 ✅** As a Dev, each project's SQLite databases get a real `.dump` backup (schema + data as
  portable SQL text), namespaced by project so a same-named db file doesn't collide, and sqlite3 is
  never invoked against a path that doesn't actually exist. *(verified by
  `ResolveBackupFilePath_places_sql_under_databases_sqlite_subfolder`,
  `ResolveBackupFilePath_namespaces_by_project_so_same_named_db_does_not_collide`,
  `BackupOne_fails_without_invoking_sqlite3_when_the_database_file_is_missing` + the wider
  `SqliteBackupServiceTests`.)*
- **M.LNCH-US-D6 ✅** As a Dev, `%APPDATA%\MindAttic` (Vault settings and credentials) is zipped into
  the same dated folder as the file/SQL/SQLite backups, degrading to a reported no-op rather than
  a failure when nothing has ever seeded Vault settings on this machine. *(verified by
  `Zip_reports_ok_without_writing_a_file_when_the_source_folder_is_missing`,
  `Zip_creates_a_real_zip_from_the_source_directory`, `ResolveZipPath_places_AppData_zip_directly_under_the_target_folder`.)*
- **M.LNCH-US-D7 ✅** As a Dev, `backup --keep <N>` prunes dated folders beyond the newest N, pruning a
  date's `_a`..`_z` collision siblings together with it rather than individually. *(verified by
  `ResolveStaleFolders_keeps_newest_N_dates`,
  `ResolveStaleFolders_prunes_collision_siblings_together_with_their_date`,
  `ResolveStaleFolders_prunes_nothing_when_under_the_limit`,
  `ResolveStaleFolders_treats_keep_zero_or_negative_as_keep_nothing`.)*
- **M.LNCH-US-D8 ✅** As a Dev, `restore --from <folder>` restores AppData, every SQL Server `.bak` and
  every SQLite dump found there — including the "database already exists" case, which is dropped to
  `SINGLE_USER` and force-replaced rather than left as a conflict. *(verified by
  `BuildRestoreSql_moves_a_single_data_and_log_file_to_the_mdf_ldf_names`,
  `BuildRestoreSql_numbers_a_second_data_and_log_file_as_ndf_and_suffixed_ldf`,
  `RestoreOne_moves_existing_database_to_single_user_before_restoring`,
  `RestoreOne_skips_single_user_when_the_database_does_not_exist`,
  `RestoreOne_reports_failure_without_throwing_when_restore_exits_nonzero` + the wider
  `SqlRestoreServiceTests`/`SqliteRestoreServiceTests`/`AppDataBackupServiceTests` (`Restore_*`).)*
- **M.LNCH-US-D9 ✅** As a Dev, the nightly backup Scheduled Task's status (registered, enabled, last
  run, next run) is visible and toggleable from inside Launcher instead of only in Task Scheduler's
  own UI, and every task this app creates lives under `\MindAttic\Launcher\`, per
  `MindAttic.HouseRules.md#HOUSE-LAW-11`. *(verified by
  `ParseQueryCsv_reads_enabled_state_last_run_and_next_run`,
  `ParseQueryCsv_reports_disabled_when_status_is_Disabled`,
  `BuildQueryArguments_targets_the_folder_qualified_task_name`.)*
- **M.LNCH-US-D10 ✅** As a Dev, `bootstrap` installs `sqlcmd` and SQL Server LocalDB via winget, trying
  each fallback id in order, without reinstalling something already on PATH. *(verified by
  `EnsureHard_skips_install_when_the_command_already_exists`,
  `EnsureHard_installs_via_the_first_winget_id_that_succeeds`,
  `EnsureHard_falls_through_to_the_next_winget_id_when_the_first_fails`.)*
- **M.LNCH-US-D11 ✅** As a Dev, `bootstrap` clones every `wip`-tagged repo not already present under
  the workspace root, reusing the same GitHub-topic discovery the roster filter already uses rather
  than a hand-maintained repo list. *(verified by
  `CloneAll_skips_a_repo_already_present_without_invoking_git`,
  `DeriveRepoName_takes_the_leaf_after_the_last_slash`, `DeriveCloneUrl_builds_an_https_github_url`.)*

## Epic E — Roster & discovery
- **M.LNCH-US-E1 ✅** As a Dev, new git repos under the workspace root are surfaced for adding, while
  registered or ignored ones are excluded. *(verified by
  `FindUnregistered_returns_only_unknown_git_repos_sorted`,
  `FindUnregistered_matches_registered_paths_with_trailing_separator`,
  `FindUnregistered_returns_empty_when_root_missing`, `FindUnregistered_tolerates_null_collections`.)*
- **M.LNCH-US-E2 ✅** As a Dev, a tab title strips the shared `MindAttic.` prefix (case-insensitively),
  preferring an explicit alias, so the tab strip stays readable. *(verified by
  `TabTitle_strips_the_shared_MindAttic_prefix`, `TabTitle_prefers_an_explicit_alias_over_the_name`,
  `TabTitle_leaves_names_without_the_prefix_untouched`, `TabTitle_strips_the_prefix_case_insensitively`.)*
- **M.LNCH-US-E3 ✅** As a Dev, adding a discovered project picks a tab color and writes a matching WT
  scheme idempotently (no duplicate or clobbered schemes). *(verified by
  `Insert_adds_scheme_preserving_existing_and_staying_valid_json`,
  `Insert_is_idempotent_when_scheme_already_present`, `Insert_handles_an_empty_schemes_array`,
  `Insert_ignores_a_same_named_profile_outside_the_schemes_array`,
  `Insert_returns_false_when_no_schemes_array` + `ColorPaletteTests`.)*
- **M.LNCH-US-E4 ✅** As a Dev, the roster shows only repos I've tagged `work-in-progress` on GitHub,
  degrading to the full roster (with a notice) when no token is set or the fetch fails, so tagging on
  GitHub is my one lever for what appears. *(verified by
  `FetchAsync_returns_lowercased_full_names_of_repos_carrying_the_topic`,
  `FetchAsync_paginates_until_a_short_page`, `FetchAsync_throws_on_a_non_success_response`,
  `Sorted_with_null_tagged_set_is_unfiltered`, `Sorted_keeps_only_projects_whose_repo_is_tagged`,
  `Sorted_excludes_a_project_with_no_parseable_github_remote_once_a_filter_is_active`.)*
- **M.LNCH-US-E5 ✅** As a Dev, an archived repo never appears in the roster even if it still carries the
  topic, and the launcher strips that stale tag on GitHub, or tells me once that my token can't,
  without repeating a refused request. *(verified by
  `FetchAsync_excludes_archived_repos_even_if_still_tagged`,
  `FetchAsync_strips_the_stale_topic_from_an_archived_repo_on_github`,
  `FetchAsync_does_not_strip_the_topic_from_an_archived_repo_that_lacks_it`,
  `FetchAsync_stops_stripping_after_github_refuses_and_flags_it`.)*

- **M.LNCH-US-E6 🟡** As a Dev, a Workflow menu item shows open tickets across the workspace, each
  repo's local-only work and GitHub state, and the ticket-log checks, through the MindAttic.Workflow
  library. The library calls are tested there; the menu itself has no test yet.

## Epic F — Settings persistence
- **M.LNCH-US-F1 ✅** As a Dev, my settings round-trip through Vault and unknown top-level / per-project
  keys are preserved across a Save. *(verified by `Save_persists_settings_round_trip`,
  `Save_preserves_unknown_top_level_and_per_project_keys`,
  `Load_returns_defaults_when_vault_is_empty`.)*
- **M.LNCH-US-F2 ✅** As a Dev, on first run with an empty Vault, my legacy `settings.json` seeds Vault
  once. *(verified by `Load_seeds_from_legacy_file_when_vault_is_empty`.)*

## Epic G — Freshness
- **M.LNCH-US-G2 ✅** As a Dev, the menu warns me when the running binary is older than the latest
  commit so I know to republish, flooring a same-day lag to zero and comparing across time-zone
  offsets. *(verified by `Evaluate_reports_whole_days_behind`,
  `Evaluate_returns_null_when_build_is_newer_than_head`, `Evaluate_returns_null_when_build_equals_head`,
  `Evaluate_floors_a_same_day_lag_to_zero_days`, `Evaluate_compares_across_time_zone_offsets`.)*

## Priority backlog
Dependency-ordered toward the headline goal (a frictionless single-binary workspace orchestrator):
1. **M.LNCH-US-A5** (🟡→✅) — make the menu-driven "Open Project Tab" flow testable (extract a pure tab
   spec from `OpenProjectMenu`) and verify it, closing the largest untested surface.
2. ⬜ Integration smoke test for the interactive menus (Backup / Commit / Settings)
   that today are exercised only by hand.
3. ⬜ End-to-end `mindattic commit` exit-code assertions over a temp git repo.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| M.LNCH-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| M.LNCH-T2 | 2026-10-12 | fix | closed | Archived repos still tagged work-in-progress: strip the stale topic on GitHub, and tell the user once (instead of retrying silently) when the token can't edit topics | M.LNCH-US-E5, `FetchAsync_stops_stripping_after_github_refuses_and_flags_it` |
| M.LNCH-T3 | 2026-10-12 | docs | closed | Document the work-in-progress roster topic filter in README and as stories | M.LNCH-US-E4, M.LNCH-US-E5 |
| M.LNCH-T4 | 2026-10-12 | chore | closed | Add the /bughunt command; ASCII arrow in publish.ps1 output so the console doesn't garble it | — |
| M.LNCH-T5 | 2026-10-12 | feature | closed | Workflow menu item: open tickets, repo status and ticket-log checks via the MindAttic.Workflow library | M.LNCH-US-E6 |
| M.LNCH-T6 | 2026-10-13 | feature | closed | Add 'General Prompt' to Open Project Tab: opens a CLI rooted at the workspace root with every project in context | OpenProjectMenu.OpenGeneralPrompt |
| M.LNCH-T7 | 2026-10-13 | feature | closed | Menu redrawn as a Spectre live region (no flicker): clock, scrollbar, ellipsis cut-offs, spacer before < Back, Open Project Tab first | Ui/Menu.cs; M.FLOW-T9 spikes |
| M.LNCH-T8 | 2026-10-13 | chore | closed | Deploy to C:\Apps\MindAttic.Launcher like other apps: one deploy.ps1 (publish/launch/restart), generated deploy.bat + repo.path beside the exe; scripts/ removed | ExePathTests |
| M.LNCH-T9 | 2026-10-13 | feature | closed | Agent tabs git pull --ff-only before launch (Pull menu removed); Exit->Quit with spacer; Status item removed; menu redraws on resize; UTF-8 writer fix; token line hidden | GitServicePullTests |
| M.LNCH-T10 | 2026-10-13 | fix | closed | Token usage line drops zero-token models like <synthetic> and pads counts to one width (line is currently hidden) | ClaudeStatusService |
| M.LNCH-T11 | 2026-10-14 | chore | open | NUnit tests for ClaudeStatusService (Services/ClaudeStatusService.cs): JSONL usage parsing dedupes by message.id, sums input/output/cache_creation/cache_read tokens per model, skips non-assistant lines, lines with no model and malformed JSON, only reads files written today; falls back to stats-cache.json last dailyModelTokens entry with labels today (cached) vs latest: date; rate-limit line color/icon thresholds at 75 and 90 percent and bar fill clamped to 20; FormatTokens boundaries (999, 1000, 9999, 10000, 1M) and ShortModelName mapping including claude- prefix stripping; needs a seam for the hard-coded ~/.claude dir so tests use a temp folder | — |
| M.LNCH-T12 | 2026-10-14 | chore | open | NUnit tests for TopicRepoSync.FetchOrNull (Services/TopicRepoSync.cs): returns null without calling GitHub when the TokenStore has no or whitespace github token, returns the client's set when the fetch succeeds, degrades to null (full roster) on HttpRequestException, InvalidOperationException and TaskCanceledException, lets other exceptions propagate; use a temp TokenStore and a GitHubTopicRepos backed by a fake HttpMessageHandler as GitHubTopicReposTests does | — |
| M.LNCH-T13 | 2026-10-14 | chore | open | NUnit tests for GitService process methods (Services/GitService.cs) against temp git repos with a local bare remote: Commit returns the status error for a non-repo, nothing to commit for a clean tree, uses the trimmed user message or AutoMessage otherwise, reports git push failed when the remote rejects, and synced on success; Pull fast-forwards and reports a no-upstream failure that IsNoUpstream recognizes; RemoteUrl returns origin or null; Clone reports cloned vs the git error text; FetchShortStatuses maps each project to its short status | — |
| M.LNCH-T14 | 2026-10-14 | chore | open | NUnit tests for HostAgentCommand (Commands/HostAgentCommand.cs) argument resolution: --path wins over --name, unknown --name returns 1, neither returns 64, unknown --provider returns 4 instead of falling back, empty RunCommand returns 2, title falls back from --title to --name to the folder name of --path (trailing separator trimmed), NO_COLOR is stripped and --prompt appended as the last arg; PullBeforeLaunch skips non-repos and stays quiet on no-upstream; extract the pure resolution/ProcessStartInfo building into testable helpers first | — |
| M.LNCH-T15 | 2026-10-14 | chore | open | NUnit tests for the backup and restore CLI subcommands (Commands/BackupCommand.cs, Commands/RestoreCommand.cs): backup returns 1 when the target folder can't be resolved, exit code is 0 only when files, AppData, every SQL and every SQLite result succeed, --keep N prunes only the stale dated folders from ResolveStaleFolders and keeps going when a delete fails, FormatBytes unit boundaries; restore returns 2 for a missing or blank --from, skips AppData when AppData.zip is absent, finds .bak files recursively under Databases and restores into --server or the default instance, matches SQLite dumps to roster projects case-insensitively and skips unmatched ones; inject the services so tests don't hit robocopy/sqlcmd | — |
| M.LNCH-T16 | 2026-10-14 | chore | open | NUnit tests for BootstrapCommand (Commands/BootstrapCommand.cs): missing GitHub token or a null TopicRepoSync result marks the run failed without cloning, --skip-optional skips the optional-tool prompts, already-installed optional tools are skipped, any failed hard prerequisite or clone yields exit 1, FindNewestBackupFolder picks the lexically newest dated folder and returns null when the backup base is missing or unreachable, and an explicit --from is passed through to RestoreCommand; needs seams for PrerequisiteService, WorkspaceCloneService and the console prompts | — |
| M.LNCH-T17 | 2026-10-14 | chore | open | NUnit tests for CommitCommand (Commands/CommitCommand.cs): an unknown --project throws instead of silently no-oping (M.LNCH-LAW-1), a project whose path is missing or whose status is invalid sets exit 1 but the loop continues to the rest, clean repos are reported as nothing to commit and don't fail the run, a failed commit/push returns 1, the default target set is ProjectRoster.Sorted filtered by the wip topic set while --project ignores the topic filter | — |
| M.LNCH-T18 | 2026-10-14 | chore | open | NUnit tests for the Ui/Menu.cs pure helpers (make Navigate, Resolve, Thumb, Truncate, Fit internal with InternalsVisibleTo): Up/K wraps from 0 to the last row, Down/J wraps to 0, Home/End, PageUp/PageDown clamp to the ends, other keys return false; Enter on the Back sentinel row yields Back, Escape yields Back only when allowBack, custom keys are ignored on the Back row; Thumb fills the track when everything fits and scales length/offset otherwise; Truncate handles max 0, 1 and exact length with an ellipsis; Fit drops markup and escapes when too wide | — |
| M.LNCH-T19 | 2026-10-14 | chore | open | NUnit tests for HostInputPipeServer (Services/HostInputPipeServer.cs) directly via its test ctor: PipeName lowercases the provider key and appends the pid, empty payloads are not forwarded to the sink, multiple sequential client connections are each delivered, UTF-8 non-ASCII text survives, and Dispose returns promptly (under ~1s) even while waiting for a connection; today it is only exercised as a fixture inside RemoteControlBroadcasterTests | — |
| M.LNCH-T20 | 2026-10-14 | chore | open | NUnit tests for the logic-bearing menu helpers (extract to internal static and test): SettingsMenu.Mask (8 chars or fewer fully masked, otherwise first4 and last4), WorkflowMenu.GitHubCell (git error, no GitHub remote, unknown index, not found, archived, wip vs untagged), DiscoverProjectsMenu.UsedColors (first project to claim a hex wins, case-insensitive, blank TabColor ignored), BackupMenu FormatBytes/Format and ScheduledBackupMenu.Describe boundaries | — |
| M.LNCH-T21 | 2026-10-14 | fix | closed | Nightly backup saves no SQL databases: Bob found its 13 by listing every user database on (localdb)\MSSQLLocalDB, but Launcher only backs up roster-listed databases (none are listed) against localhost, and Bob's task is already unregistered; add LocalDB auto-discovery (or default) before the 00:00 run, and commit the uncommitted backup/restore/bootstrap work | 89b2e8c / SqlBackupServiceTests.DiscoverDatabases_defaults_to_localdb_and_returns_one_target_per_line |
| M.LNCH-T22 | 2026-10-14 | fix | open | Finish absorbing Bob into bootstrap: no LocalDB create/start, no --server (restore defaults to localhost), GitHub token needed before AppData is restored, clones only wip repos and never pulls existing ones, no AI CLI installs, no PATH refresh after winget, Claude desktop/Antigravity detected by the wrong command; comments name a bootstrap.ps1 that is setup.ps1 | — |
| M.LNCH-T23 | 2026-10-14 | chore | open | Retire Bob cleanly: nothing prunes R:\Backup\Bob any more, MindAttic.HouseRules.md:73 still points at Bob's task script, workspace-repos.json and the agent command catalogs still list Bob files; Bob's superseded banner is uncommitted on an archived repo | — |
| M.LNCH-T24 | 2026-10-15 | chore | closed | Remove duplicate command copies (/bughunt, /deploy, /quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| M.LNCH-T25 | 2026-10-15 | fix | closed | Bump the Launcher to MindAttic.Workflow 2.0.0: on 1.0.0 its Workflow menu reads the old markdown, which is now a pointer, so it lists no tickets | 189 Launcher tests pass on MindAttic.Workflow 2.0.0; its Workflow menu now reads the database |
| M.LNCH-T26 | 2026-10-16 | feature | closed | Mass commit (menu and commit command) aligns every commit to a workflow ticket, creating a closed one when the message names none, and commits the workflow database last | WorkspaceCommitterTests |
