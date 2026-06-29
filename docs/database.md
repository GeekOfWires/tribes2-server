# Database

The panel stores its state in a single **SQLite-format file** at `PANEL_DB_PATH`
(default `/data/panel.db`) via **EF Core**.

## Local SQLite / libSQL / Turso-compatible

Turso/libSQL are SQLite-compatible forks; a local Turso database **is** a SQLite-format file. This
project uses EF Core's SQLite provider against that file, so you get a local, file-based,
Turso-compatible database that **stays local**. Inspect it with whichever CLI you
prefer:

```bash
turso db shell /data/panel.db     # or: sqlite3 /data/panel.db  /  libsql shell
```

(There is no EF Core 10 provider for the newer Turso/Limbo engine or for libSQL today, so the
SQLite provider on the compatible file is the robust local-only choice.)

## Tables

ASP.NET Core Identity owns the `AspNet*` tables (users, roles, claims, …). The app adds:

| Table | Entity | Holds |
|-------|--------|-------|
| `AspNetUsers` (+ `IsDeveloper`, `IsActive`, `CreatedAt`) | `ApplicationUser` | Accounts. `IsDeveloper` is the additive [Developer capability](web-panel.md#the-developer-capability). |
| `ServerSettings` (single row, `Id=1`) | `ServerSettings` | `Configured`, `AutoStart`, `LaunchParams`, `Ruleset`. |
| `AuditLog` | `AuditEntry` | Every privileged action (actor, role, action, target, detail, success, ts). |
| `Crashes` | `CrashReport` | Unexpected game exits (timestamps, exit code, fault addr/instr/module, launch params, console tail + CRASHLOG). |
| `FileEdits` | `FileEdit` | Every panel file change with the **pre-change snapshot** for revert. |

## How schema is created & migrated

There are no EF migration files; schema is applied at startup by
[`Bootstrap.cs`](../panel/Bootstrap.cs):

1. `EnsureCreatedAsync()` creates the schema on a fresh database.
2. Because `EnsureCreated` won't alter a **pre-existing** database volume, Bootstrap also runs
   **defensive** `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE … ADD COLUMN` statements (ignoring
   "already exists") for tables/columns added over time (`Crashes`, `FileEdits`, `IsDeveloper`,
   `Ruleset`). This lets an existing `/data/panel.db` pick up new features without manual steps.
3. The single `ServerSettings` row and the initial **root** user are seeded (root only if none
   exists, from `ROOT_USERNAME`/`ROOT_PASSWORD`).

## Backups

Back up the whole **`/data`** volume (database + Data-Protection keys + TLS material). A hot copy
of `panel.db` is usually fine for this low-write workload; for a guaranteed-consistent copy use
`sqlite3 /data/panel.db ".backup /data/panel-backup.db"`.

## See also
- [Configuration reference](configuration.md) · [Web panel & roles](web-panel.md)
- Back to [docs index](README.md)
