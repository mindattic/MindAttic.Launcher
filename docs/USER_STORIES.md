---
codex: 1
project: MindAttic.Launcher
code: M.LNCH
layer: stories
status: living
updated: 2026-10-14
---
<!-- GENERATED from MindAttic.Tickets/data/Tickets.db by `tickets db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the tickets CLI. -->

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
| M.LNCH-T11 | 2026-10-14 | chore | closed | NUnit tests for ClaudeStatusService (Services/ClaudeStatusService.cs): JSONL usage parsing dedupes by message.id, sums input/output/cache_creation/cache_read tokens per model, skips non-assistant lines, lines with no model and malformed JSON, only reads files written today; falls back to stats-cache.json last dailyModelTokens entry with labels today (cached) vs latest: date; rate-limit line color/icon thresholds at 75 and 90 percent and bar fill clamped to 20; FormatTokens boundaries (999, 1000, 9999, 10000, 1M) and ShortModelName mapping including claude- prefix stripping; needs a seam for the hard-coded ~/.claude dir so tests use a temp folder | 6535882 |
| M.LNCH-T12 | 2026-10-14 | chore | closed | NUnit tests for TopicRepoSync.FetchOrNull (Services/TopicRepoSync.cs): returns null without calling GitHub when the TokenStore has no or whitespace github token, returns the client's set when the fetch succeeds, degrades to null (full roster) on HttpRequestException, InvalidOperationException and TaskCanceledException, lets other exceptions propagate; use a temp TokenStore and a GitHubTopicRepos backed by a fake HttpMessageHandler as GitHubTopicReposTests does | df3fcf7 |
| M.LNCH-T13 | 2026-10-14 | chore | closed | NUnit tests for GitService process methods (Services/GitService.cs) against temp git repos with a local bare remote: Commit returns the status error for a non-repo, nothing to commit for a clean tree, uses the trimmed user message or AutoMessage otherwise, reports git push failed when the remote rejects, and synced on success; Pull fast-forwards and reports a no-upstream failure that IsNoUpstream recognizes; RemoteUrl returns origin or null; Clone reports cloned vs the git error text; FetchShortStatuses maps each project to its short status | 66a6cf1 |
| M.LNCH-T14 | 2026-10-14 | chore | closed | NUnit tests for HostAgentCommand (Commands/HostAgentCommand.cs) argument resolution: --path wins over --name, unknown --name returns 1, neither returns 64, unknown --provider returns 4 instead of falling back, empty RunCommand returns 2, title falls back from --title to --name to the folder name of --path (trailing separator trimmed), NO_COLOR is stripped and --prompt appended as the last arg; PullBeforeLaunch skips non-repos and stays quiet on no-upstream; extract the pure resolution/ProcessStartInfo building into testable helpers first | 72774ad |
| M.LNCH-T15 | 2026-10-14 | chore | closed | NUnit tests for the backup and restore CLI subcommands (Commands/BackupCommand.cs, Commands/RestoreCommand.cs): backup returns 1 when the target folder can't be resolved, exit code is 0 only when files, AppData, every SQL and every SQLite result succeed, --keep N prunes only the stale dated folders from ResolveStaleFolders and keeps going when a delete fails, FormatBytes unit boundaries; restore returns 2 for a missing or blank --from, skips AppData when AppData.zip is absent, finds .bak files recursively under Databases and restores into --server or the default instance, matches SQLite dumps to roster projects case-insensitively and skips unmatched ones; inject the services so tests don't hit robocopy/sqlcmd | BackupRestoreCommandTests / 607f026 |
| M.LNCH-T16 | 2026-10-14 | chore | closed | NUnit tests for BootstrapCommand (Commands/BootstrapCommand.cs): missing GitHub token or a null TopicRepoSync result marks the run failed without cloning, --skip-optional skips the optional-tool prompts, already-installed optional tools are skipped, any failed hard prerequisite or clone yields exit 1, FindNewestBackupFolder picks the lexically newest dated folder and returns null when the backup base is missing or unreachable, and an explicit --from is passed through to RestoreCommand; needs seams for PrerequisiteService, WorkspaceCloneService and the console prompts | 41f4f5f |
| M.LNCH-T17 | 2026-10-14 | chore | closed | NUnit tests for CommitCommand (Commands/CommitCommand.cs): an unknown --project throws instead of silently no-oping (M.LNCH-LAW-1), a project whose path is missing or whose status is invalid sets exit 1 but the loop continues to the rest, clean repos are reported as nothing to commit and don't fail the run, a failed commit/push returns 1, the default target set is ProjectRoster.Sorted filtered by the wip topic set while --project ignores the topic filter | 5c139ab |
| M.LNCH-T18 | 2026-10-14 | chore | closed | NUnit tests for the Ui/Menu.cs pure helpers (make Navigate, Resolve, Thumb, Truncate, Fit internal with InternalsVisibleTo): Up/K wraps from 0 to the last row, Down/J wraps to 0, Home/End, PageUp/PageDown clamp to the ends, other keys return false; Enter on the Back sentinel row yields Back, Escape yields Back only when allowBack, custom keys are ignored on the Back row; Thumb fills the track when everything fits and scales length/offset otherwise; Truncate handles max 0, 1 and exact length with an ellipsis; Fit drops markup and escapes when too wide | adadad7 |
| M.LNCH-T19 | 2026-10-14 | chore | closed | NUnit tests for HostInputPipeServer (Services/HostInputPipeServer.cs) directly via its test ctor: PipeName lowercases the provider key and appends the pid, empty payloads are not forwarded to the sink, multiple sequential client connections are each delivered, UTF-8 non-ASCII text survives, and Dispose returns promptly (under ~1s) even while waiting for a connection; today it is only exercised as a fixture inside RemoteControlBroadcasterTests | a642c75 |
| M.LNCH-T20 | 2026-10-14 | chore | closed | NUnit tests for the logic-bearing menu helpers (extract to internal static and test): SettingsMenu.Mask (8 chars or fewer fully masked, otherwise first4 and last4), WorkflowMenu.GitHubCell (git error, no GitHub remote, unknown index, not found, archived, wip vs untagged), DiscoverProjectsMenu.UsedColors (first project to claim a hex wins, case-insensitive, blank TabColor ignored), BackupMenu FormatBytes/Format and ScheduledBackupMenu.Describe boundaries | f5b827e |
| M.LNCH-T21 | 2026-10-14 | fix | closed | Nightly backup saves no SQL databases: Bob found its 13 by listing every user database on (localdb)\MSSQLLocalDB, but Launcher only backs up roster-listed databases (none are listed) against localhost, and Bob's task is already unregistered; add LocalDB auto-discovery (or default) before the 00:00 run, and commit the uncommitted backup/restore/bootstrap work | 89b2e8c / SqlBackupServiceTests.DiscoverDatabases_defaults_to_localdb_and_returns_one_target_per_line |
| M.LNCH-T22 | 2026-10-14 | fix | closed | Finish absorbing Bob into bootstrap: no LocalDB create/start, no --server (restore defaults to localhost), GitHub token needed before AppData is restored, clones only wip repos and never pulls existing ones, no AI CLI installs, no PATH refresh after winget, Claude desktop/Antigravity detected by the wrong command; comments name a bootstrap.ps1 that is setup.ps1 | 2ad8597 |
| M.LNCH-T23 | 2026-10-14 | chore | open | Retire Bob cleanly: nothing prunes R:\Backup\Bob any more, MindAttic.HouseRules.md:73 still points at Bob's task script, workspace-repos.json and the agent command catalogs still list Bob files; Bob's superseded banner is uncommitted on an archived repo | — |
| M.LNCH-T24 | 2026-10-15 | chore | closed | Remove duplicate command copies (/bughunt, /deploy, /quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| M.LNCH-T25 | 2026-10-15 | fix | closed | Bump the Launcher to MindAttic.Workflow 2.0.0: on 1.0.0 its Workflow menu reads the old markdown, which is now a pointer, so it lists no tickets | 189 Launcher tests pass on MindAttic.Workflow 2.0.0; its Workflow menu now reads the database |
| M.LNCH-T26 | 2026-10-16 | feature | closed | Mass commit (menu and commit command) aligns every commit to a workflow ticket, creating a closed one when the message names none, and commits the workflow database last | WorkspaceCommitterTests |
| M.LNCH-T27 | 2026-10-16 | fix | closed | Workflow database sync commits cite a ticket the run created, and a database-only change gets its own ticket, so the workspace commit-msg hook accepts them | WorkspaceCommitterTests |
| M.LNCH-T28 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 8 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
| M.LNCH-T29 | 2026-10-16 | fix | closed | Mass commit never stages Prose's local agent state (.prose/agent-session.json*, agent-queue.json*, quicksave.md, archive/), which Prose's own .gitignore keeps out of every repo; .prose/commands stays tracked | WorkspaceCommitterTests.Prose_local_agent_state_is_never_committed_and_alone_reads_as_clean |
| M.LNCH-T30 | 2026-10-16 | fix | closed | Mass commit failed in repos that already .gitignore Prose's agent state (git add refuses an exclude pathspec naming an ignored path); stage with git add -A, then unstage the state with git reset | WorkspaceCommitterTests.A_repo_that_already_gitignores_prose_state_still_commits |
| M.LNCH-T31 | 2026-10-16 | fix | closed | Mass commit leaves a repo committed but unpushed when its remote moved ahead: on a rejected push, rebase onto the remote (autostash) and push again; a conflicting rebase is aborted and reported, leaving the local commit as it was | WorkspaceCommitterTests.A_remote_that_moved_ahead_is_rebased_onto_and_the_commit_still_pushes, A_conflicting_rebase_is_undone_and_the_local_commit_is_kept |
| M.LNCH-T32 | 2026-10-16 | fix | closed | Mass commit skips a clean repo that is still ahead of its upstream (an earlier push failed), so it stays unpushed forever; push those too, with the same rebase-on-rejection | WorkspaceCommitterTests.A_clean_repo_left_ahead_by_an_earlier_failed_push_is_pushed |
| M.LNCH-T33 | 2026-10-16 | chore | closed | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | c1e4286 |
| M.LNCH-T34 | 2026-10-16 | chore | closed | Run the full unit test suite and fix every failure (each under its own fix ticket) until it passes; put the passing run in --ref. No test project: close this wont-do saying so | full suite passed 359/359, 0 failed, 2026-10-22 (dotnet test MindAttic.Launcher.Tests, worktree at main) |
| M.LNCH-T35 | 2026-10-16 | chore | closed | Clean up code and docs: remove dead code, stale comments and outdated or duplicated docs, and simplify where behaviour doesn't change (/simplify); tests stay green | da298ac |
| M.LNCH-T36 | 2026-10-16 | docs | closed | Bring README.md up to date with the code as it is now (what it is, setup, usage, commands, current state), following codex-standard/README-STYLE.md; README.htm regenerated if the repo has one | b4bcd78 |
| M.LNCH-T37 | 2026-10-16 | fix | closed | A push GitHub refuses because the repo is archived (read-only) is reported as an expected 'archived on GitHub; kept local' notice, not a push failure, so an archived repo (MindAttic.Bob) stops failing every commit run and its exit code | WorkspaceCommitterTests.An_archived_remote_keeps_commits_local_without_failing_the_run |
| M.LNCH-T38 | 2026-10-18 | feature | closed | Follow the Orchestrator's billing mode (M.ORCH billing switch): ProviderCredentials pushes ANTHROPIC_API_KEY from the Vault only when the mode is API (today it pushes it whenever no claude.ai login exists at launch, so a later /login still bills the key), and a hosted Claude tab can be restarted in place on request over its pipe: end the claude process between turns and start claude --resume <session id> with the new mode's environment, so the conversation carries on billing the right account | 33c79cc |
| M.LNCH-T39 | 2026-10-18 | fix | closed | bootstrap installs the Claude Code, Codex and Antigravity CLIs but not the Kimi Code CLI (kimi) that the default Kimi provider runs, so a Kimi tab fails on a freshly bootstrapped machine; needs a confirmed unattended install source (uv tool / pip / winget) before adding it to PrerequisiteService.AiClis | ff1504c |
| M.LNCH-T40 | 2026-10-18 | feature | closed | Onboarding: setup.ps1 and a new adopt-repos command tag every GitHub repo the token can see as wip so the roster is full on first launch | — |
| M.LNCH-T41 | 2026-10-18 | feature | closed | A new Prompt.TextOrCancel UI helper lets Esc cancel the commit-message prompt in CommitMenu instead of being silently ignored | — |
| M.LNCH-T42 | 2026-10-18 | chore | closed | Stop registering MindAtticLauncherNightlyBackup (Services/ScheduledTaskService, Menus/ScheduledBackupMenu, tools/register-nightly-backup-task.ps1); the backup menu points at the WatchDog job (job now in MindAttic.WatchDog M.WD-T6) | 9148a9c |
| M.LNCH-T43 | 2026-10-19 | chore | wont-do | Convert tracked source files to LF line endings via .gitattributes, per HOUSE-LAW-13 (low priority) | — |
| M.LNCH-T44 | 2026-10-20 | feature | closed | Read the orchestrator's pace status from its database: MindAttic.Orchestrator now keeps it as the rl-status row of data/orchestrator.db's state table (M.ORCH-T526), found as OrchestratorDb.StatePath finds it (MINDATTIC_ORCHESTRATOR_DB, else <workspace>\MindAttic.Orchestrator\data\orchestrator.db, else %APPDATA%\MindAttic\MindAttic.Orchestrator\orchestrator.db); ClaudeStatusService should read that row (read-only, short busy timeout, newer timestamp of row and file wins) and fall back to ~/.claude/rl-status.json, which the orchestrator keeps exporting until every outside reader has switched | ClaudeStatusServiceTests (31ad24b) |
| M.LNCH-T45 | 2026-10-20 | docs | closed | Publish MindAttic.Launcher docs to GitHub Pages via the shared Toolkit DocFX pipeline: run adopt-docs-site.ps1 (README.md as home page, docs/*.md as articles, API reference only where the code builds in CI), enable Pages with build_type=workflow, set the repo homepage URL, retire tools/build-readme.ps1 and README.htm, confirm the docs Actions run is green and https://mindattic.github.io/MindAttic.Launcher/ returns 200. Depends on MAT-T8. | f493703 |
| M.LNCH-T46 | 2026-10-20 | chore | closed | Follow MindAttic.Workflow's rename to MindAttic.Tickets (M.FLOW-T120): MindAttic.Tickets package and namespaces, the tickets CLI, the Tickets menu, data/Tickets.db, docs and tests | 555c23b |
| M.LNCH-T47 | 2026-10-20 | feature | closed | Open Project Tab: collapse dot-named projects (MindAttic.*, GTFO.*) into per-prefix disclosure rows (▸); Esc pops one level before exiting | — |
| M.LNCH-T48 | 2026-10-20 | feature | closed | Per-CLI Claude login in the Launcher's own menu (follows M.ORCH-T559): when opening a Claude tab, offer the per-CLI logins signed in under %USERPROFILE%\.claude-accounts\<email> (orchestrator login --account <email>) beside the global login, and start the tab's claude with CLAUDE_CONFIG_DIR set to the chosen account's folder, as 'orchestrator start <repo> --account <email>' already does by wrapping the host in a PowerShell that sets it; without a choice the tab uses the global login exactly as today | 67c9897 |
| M.LNCH-T49 | 2026-10-21 | docs | closed | Update USER_STORIES.md | — |
| M.LNCH-T50 | 2026-10-22 | docs | closed | Update USER_STORIES.md | — |
| M.LNCH-T51 | 2026-10-22 | feature | wont-do | Launcher tabs pick a Claude login too: opening a project's Claude tab from the Launcher (menus and host) runs it under the login orchestrator accounts pick chooses (setting CLAUDE_CONFIG_DIR for the tab; no variable for the global login, and no change when the orchestrator isn't installed), and host takes --account <email> to name one, so tabs opened outside the dashboard also spread one CLI per Claude Team account | Superseded by M.LNCH-T53 |
| M.LNCH-T52 | 2026-10-22 | feature | closed | Move a running CLI to another login: the host pipe's restart-resume request takes an optional configDir (empty = the global login), and the host restarts that tab's CLI at its next idle point with claude --resume under that CLAUDE_CONFIG_DIR, so the same conversation carries on billing the other account | 256ab10 |
| M.LNCH-T53 | 2026-10-22 | feature | closed | Ask which account a Claude tab belongs to: when the Launcher opens a Claude tab (menus and host) and more than one Claude login is signed in (the global ~/.claude login plus each ~/.claude-accounts per-CLI login), it lists them by email with each one's 5h/week left and which already have a CLI, preselects orchestrator accounts pick's choice (Auto), and starts the tab's CLI under the chosen login's CLAUDE_CONFIG_DIR (none for the global login); host --account <email> skips the question, a single login skips it, and the tab title shows the account, so one computer runs several CLIs each logged in as a different person. Supersedes M.LNCH-T51 | 5ca3e9c |
| M.LNCH-T54 | 2026-10-22 | feature | closed | A Launcher scoped to one person: MindAttic.Launcher --account <email> runs the whole Launcher as that Claude login; every Claude tab it opens, from any repo, sets that login's CLAUDE_CONFIG_DIR explicitly in the tab's own command (Windows Terminal tabs don't inherit the Launcher's environment), so every CLI from it bills that person; its window and tab titles show the login's alias (read from mindattic-account.json in the login's folder, else the email); it never asks which account (that question is only for an unscoped Launcher, M.LNCH-T53); and an account that isn't signed in yet is signed in first (orchestrator login --account) before the Launcher opens | def9cdc |
| M.LNCH-T55 | 2026-10-22 | feature | closed | A terminal window per person in the Launcher: Claude tabs the Launcher opens (and a scoped Launcher, M.LNCH-T54) go into the Windows Terminal window named for the tab's login (wt -w <alias>) with the login's initials badge in the title and its tab color, the same rule as the Orchestrator (M.ORCH-T616), read from mindattic-account.json in the login's folder | d4ffb0a |
| M.LNCH-T56 | 2026-10-22 | feature | closed | Initials icons on terminal tabs: each Claude login gets its own Windows Terminal profile through a Terminal fragment (%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\MindAttic) whose icon is a generated image of its initials (JR, AR, RD) on its tab color, and its tabs open with that profile (wt new-tab -p), so ownership shows as an icon as well as the [JR] title badge; regenerated when a login's alias or initials change | LoginTerminalProfilesTests; 5e9abea |
| M.LNCH-T57 | 2026-10-22 | fix | closed | A failed SQLite restore deleted the live database; move it aside and put it back on failure | RestoreOne_puts_the_existing_database_back_when_the_restore_fails |
| M.LNCH-T58 | 2026-10-22 | fix | closed | Restoring SQLite dumps put every dump of a project over its first database file; match dumps by file stem | RestoreCommand.RestoreSqliteDatabases |
| M.LNCH-T59 | 2026-10-22 | fix | closed | SQLite backups of same-stem databases in one project overwrote each other's dump | Backup_numbers_a_dump_whose_file_stem_collides_with_an_earlier_one |
| M.LNCH-T60 | 2026-10-22 | fix | closed | A failed AppData zip left a corrupt AppData.zip in the dated backup folder | AppDataBackupService.Zip |
| M.LNCH-T61 | 2026-10-22 | fix | closed | One malformed line in a Claude session log dropped the rest of that file's usage from the status line | ClaudeStatusService.ParseJsonlUsage |
| M.LNCH-T62 | 2026-10-22 | fix | closed | backup --keep N prunes older backup folders even when the new backup failed, so a run of failing backups deletes the good ones; prune only after the new backup succeeds | 7e51533 |
| M.LNCH-T63 | 2026-10-22 | chore | closed | Finish the bug hunt M.LNCH-T33 closed early: a full /bughunt pass over what it didn't re-read (Ui/, the Menus/ files beyond the restore/backup ones, the pipe and interop services, the shell scripts), fixing each real bug under its own fix ticket, then one more full pass; close only when a full pass finds nothing | da9b5f1 |
| M.LNCH-T64 | 2026-10-22 | feature | closed | host --resume <session>: let MindAttic.Launcher host start a Claude tab by resuming a given Claude session (claude --resume <id>) instead of a fresh one, so the Orchestrator's embedded panes (M.ORCH-T618) reopened after a dashboard restart run through the Launcher host again and keep its input pipe, control pipe, restart-resume and account moves, instead of running bare claude --resume | ClaudeResumeTests (39ce294) |
| M.LNCH-T65 | 2026-10-22 | feature | closed | Ask which account before opening the tab: when an unscoped Launcher opens a Claude tab and more than one login is signed in, ask which account in the Launcher itself (the same list as M.LNCH-T53's in-tab question, Auto preselected) before opening the tab, then open it in that person's window with their badge and color (M.LNCH-T55) and pass the choice to host as --config-dir, so the tab never asks again; a single signed-in login and a scoped Launcher skip the question as now | ClaudeTabLoginTests (67c9897) |
| M.LNCH-T66 | 2026-10-23 | fix | closed | Move the Launcher onto MindAttic.Tickets 3.0.0 (C:\LocalNuGet; M.TIX-T131) and redeploy it BEFORE the ticket database moves to SQL Server: WorkspaceCommitter adds and closes a ticket per commit through TicketsDb, and on 2.0.0 those writes would land in data/Tickets.db, which after tickets db server migrate is only the SQL Server copy (rewritten after every change), so they would be lost. Bump MindAttic.Launcher.csproj, build, run its tests, deploy.ps1; then tell the MindAttic.Orchestrator window so it runs the migration. Also remove the stale worktree D:\Projects\MindAttic.worktrees\MindAttic.Launcher\M.TIX-T131 (branch orch/m.tix-t131-launcher, unchanged) | 0ec10de |
| M.LNCH-T67 | 2026-10-23 | feature | closed | Take the orchestrator's emergency-failover restart (M.ORCH-T664, docs/ORCHESTRATION.md 'Emergency failover to the API key'): a control-pipe restart-resume with "reason":"failover" bills its 'billing' (api: ANTHROPIC_API_KEY set to the Vault's Claude key; team: removed) for that CLI alone, never dropped because billing.json's mode differs, kept for the CLI's later restarts until another request says otherwise, under its configDir; with "now":true restart at once (its turn already ended at a limit error) instead of waiting for the idle point | 7181832 |
| M.LNCH-T68 | 2026-10-23 | fix | closed | Setup menu: 'blank to clear' on Alias/Description/Color fields could never clear, since a blank Enter returned the prompt's default (the current value); blank now clears and Esc cancels | da9b5f1 |
| M.LNCH-T69 | 2026-10-23 | feature | closed | Tab titles colon badge (from M.ORCH-T632): LoginLook.Badge() 'JR: Repo [Claude]' instead of '[JR] Repo [Claude]'; Project.cs NamePrefix (and ColorPalette's) strips GTFO. and Unity. as well as MindAttic.; update LoginLookTests and any Project/WindowsTerminalLauncher tests asserting the bracket format. The Orchestrator side landed in e4c46e4. | 8069dc7 |
| M.LNCH-T70 | 2026-10-23 | fix | open | Restart-resume that moves a Claude tab to another login (control pipe configDir, account moves/failovers) must also set CLAUDE_CODE_OAUTH_TOKEN for the new folder from the Orchestrator's sealed Vault entry (%APPDATA%\MindAttic\ClaudeLogins\tokens.json, keyed by email: {sealed: DPAPI CurrentUser base64 with entropy 'MindAttic.Orchestrator.ClaudeLoginToken', minted, expires}) or remove it, and unset ANTHROPIC_AUTH_TOKEN; today the tab keeps the token it started with (M.ORCH-T696), and the token, not CLAUDE_CONFIG_DIR, picks the account, so a move doesn't take effect | M.ORCH-T696 |
| M.LNCH-T71 | 2026-10-23 | feature | closed | Resolve the Claude account once at Launcher startup (from current org members and their eligibility via orchestrator members --json, the SQL Server-backed store per M.ORCH-T658) instead of the stale local .claude-accounts folder enumeration, and drop the redundant per-tab Open it on which Claude account? screen (ClaudeAccountMenu/ClaudeTabLogin, M.LNCH-T65) now that --account-style scoping (M.LNCH-T54) covers the whole session; the host's own --config-dir-less fallback prompt (M.LNCH-T53) is unaffected | WhoIsUsing/OrchestratorMembers |
| M.LNCH-T72 | 2026-10-23 | feature | closed | Who's-using startup prompt offers API Key alongside real org members: picking it switches the dashboard's billing mode to the Vault key (orchestrator billing api) and runs the Launcher unscoped, so every CLI it opens bills the organization instead of a person's seat | — |
| M.LNCH-T73 | 2026-10-23 | docs | closed | Update USER_STORIES.md | — |
| M.LNCH-T74 | 2026-10-24 | fix | open | MindAttic.Cryptography never shows in the Launcher roster (found by MAT-T26 sweep): its settings.json repoUrl and its local origin are still https://github.com/mindattic/Cryptography.git, the pre-rename name; GitHub now calls it mindattic/MindAttic.Cryptography (tagged wip), so ProjectRoster's wip filter (GitHubRepoRef.Parse vs TopicRepoSync's set) never matches. Fix: when a project's repoUrl isn't in the tagged set, resolve the repo's current full name via the GitHub API (a renamed repo redirects) and heal repoUrl in settings.json, like RepoUrlBackfill does for blank ones; also git remote set-url origin in D:\Projects\MindAttic\MindAttic.Cryptography so docs/workspace-repos.json regenerates with the new URL | — |
| M.LNCH-T75 | 2026-10-24 | fix | open | Scheduled Backup menu still tells the user the nightly backup lives in 'MindAttic.WatchDog' (Menus/ScheduledBackupMenu.cs lines 22 and 29, README.md lines 205 and 315): the project is MindAttic.Scheduler since M.SCHD-T17. Say MindAttic.Scheduler in the user-facing text and README (found by MAT-T26 sweep) | — |
| M.LNCH-T76 | 2026-10-24 | docs | closed | Update USER_STORIES.md | — |
| M.LNCH-T77 | 2026-10-25 | feature | closed | When this Launcher is running 'inside' the Orchestrator (hosted/embedded, as opposed to its own standalone window), opening a new CLI routes into the Orchestrator's embedded Terminals panel (requires MindAttic.Orchestrator's side of this, see the linked ticket) as a new tab beside the existing ones, instead of opening a separate Launcher-owned window. Needs a way for this process to detect 'inside the Orchestrator' mode (e.g. a flag/mode passed when the Orchestrator starts/hosts it, or a handshake over the existing pipe the Orchestrator's --show deep-link already uses). The existing spawn entry point is Commands/HostAgentCommand.cs -- extend it to route there when hosted, falling back to its current standalone-window behavior otherwise. | WindowsTerminalLauncherTests |
| M.LNCH-T78 | 2026-10-25 | feature | closed | Relocate reason on the control pipe (Dock/Undock): a restart-resume request with reason:relocate carries neither billing nor configDir and means end this CLI here once idle instead of restarting it, so the Orchestrator can reopen it resumed elsewhere; the watch loop's per-tick decision is extracted into HostAgentCommand.NextAction (Wait/Drop/Relaunch/Relocate) for unit testing without a real claude.exe process | 6267f12 |
| M.LNCH-T79 | 2026-10-25 | feature | closed | Generalize Project.ShortName to drop a dot-prefix only when two or more known projects share it, instead of a hardcoded GTFO./MindAttic./Unity. list; fix the scoped menu's own console title (hardcoded RD: + redundant alias suffix) to use the real initials and the generalized name | 2f5fec9 |
| M.LNCH-T80 | 2026-10-25 | chore | open | Find and launch the Orchestrator as MindAttic.Orchestrator.Desktop.exe (OrchestratorCli.cs, MainMenuCommand.cs) after its exe rename (M.ORCH-T844) | — |
| M.LNCH-T81 | 2026-10-25 | fix | closed | Start the Orchestrator dashboard by its new name MindAttic.Orchestrator.Desktop.exe (M.ORCH-T844 renamed it; the old MindAttic.Orchestrator.exe is only kept as a legacy fallback) | c3f30b7 |
| M.LNCH-T82 | 2026-10-26 | feature | closed | Open CLIs through an Orchestrator TermHost when no dashboard is listening, so they appear on the dashboard's next start instead of in a Windows Terminal window (M.ORCH-T922 phase 1) | 046fb61 |
| M.LNCH-T83 | 2026-10-26 | fix | closed | A Launcher started on its own (not embedded in the dashboard) still opens agent tabs in Windows Terminal while the dashboard is running: when TermHost is installed, start every CLI in a TermHost regardless of where the Launcher runs, so it always appears in the Orchestrator dashboard (via the session registry and term-session bus kind) and survives any window closing (follow-up of M.LNCH-T82) | 480c6d7 |
| M.LNCH-T84 | 2026-10-26 | fix | closed | Launcher-started CLIs carry no login: TermHostLauncher.Arguments (Services/TermHostLauncher.cs:49-56) passes no --login or --repo, so the TermHost registers Login=null and ConfigDir from the Launcher's own env; the dashboard puts the tab on the global login's page with the wrong colour until the CLI's first turn, and the gateway treats it as the default org (wrong watchers can see it, the right org can't); pass the login and repo to TermHost (found by bughunt pass 2) | e15a9bc |
| M.LNCH-T85 | 2026-10-26 | fix | closed | TermHostLauncher.InstallDir is hard-coded to C:\Apps\MindAttic.Orchestrator (Services/TermHostLauncher.cs:14) and ignores MINDATTIC_TERMHOST, while the Installer can install under %LOCALAPPDATA%\Programs, so on such machines the standalone Launcher silently falls back to Windows Terminal; resolve the install like TermHostInstalls (found by bughunt pass 2) | f39d02d |
| M.LNCH-T86 | 2026-10-26 | fix | closed | The Launcher's TermHost path doesn't apply the API-key person's pane setup that PaneRequest.ApiKeySetup does (TerminalPanes.cs:97-104): it neither sets MINDATTIC_CLAUDE_BILLING nor strips CLAUDE_CODE_OAUTH_TOKEN/ANTHROPIC_AUTH_TOKEN, so an inherited OAuth token can take precedence over the API key for CLIs on the API-key login; mirror ApiKeySetup (found by bughunt pass 2) | 5743715 |
| M.LNCH-T87 | 2026-10-26 | fix | closed | The Launcher's ClaudeRestartGate (Services/ClaudeRestartGate.cs:~38) requires state=='idle', so a CLI whose Stop hook was cut off after the Orchestrator's 'deciding' save (M.ORCH-T1036) is never restarted or resumed; treat a 'deciding' mark older than 75s as idle, matching the Orchestrator (found by bughunt pass 3) | 0c5856a |
| M.LNCH-T88 | 2026-10-26 | fix | wont-do | AccountScope.Ensure sends the API-key login (api-key@mindattic.vault) through the claude.ai OAuth sign-in flow instead of recognizing it's Vault-key-based | 1c51228 |
| M.LNCH-T89 | 2026-10-27 | chore | open | Follow the Orchestrator dashboard exe rename (M.ORCH-T1205): MainMenuCommand starts MindAttic.Orchestrator.Desktop.Wpf.exe, falling back to the old name, and honours the Preferred dashboard setting | — |
| M.LNCH-T90 | 2026-10-27 | chore | closed | Add 1 file(s); Update 13 file(s) | — |
| M.LNCH-T91 | 2026-10-27 | fix | open | Roster lists RogueTrader.Overwatch and RogueTrader.Shared 6x each (duplicate settings.json entries, no dedup) | — |
