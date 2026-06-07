Deploy the MindAttic.Console landing page (`mindattic.com/mindatticconsole.htm`) via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Renders this repo's `README.md` through the catalog template (`template/index.template.htm`, Cyberspace theme, MindAttic.UiUx components loaded via jsDelivr) and FTPS-uploads the single-file result. One repo owns the whole FTP pipeline — there is no per-project deploy state in this folder.

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --only mindatticconsole"
```

It will:

1. Render `D:\Projects\MindAttic\MindAttic.Console\README.md` through the catalog template.
2. FTPS-upload `out/mindatticconsole.htm` to `/mindattic.com/mindatticconsole.htm`.

After running, summarize the result and flag any failures.

Notes:
- Catalog entry: `MindAttic.Deploy/projects.json` -> `projects[]` slug `mindatticconsole` (theme: Cyberspace).
- Credentials: MindAttic.Vault at `%APPDATA%\MindAttic\Deploy\ftp.json` (transitional fallback: `MindAttic.Deploy/secrets/ftp.json`, gitignored).
- MindAttic.Console is the launcher CLI itself (no Blazor/app deploy target) — this command only ships the landing page. The Console's in-app "Deploy All" menu item is a separate path that runs `MindAttic.Deploy.exe all` to deploy every project at once.
