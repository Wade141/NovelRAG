# Data model

Database: `%LOCALAPPDATA%\NovelRAG\novelrag.db`; override the data directory with `--data-dir <absolute-path>` for isolated tests or portable profiles. Timestamps use UTC round-trip text; UI displays local time.

- Projects: integer ID, unique case-insensitive name. Deletion sets Ideas.ProjectId to null rather than deleting notes.
- Ideas: monotonic AUTOINCREMENT ID, nullable project FK, content, created/updated UTC, archived flag, revision, reserved nullable type. Content is 1–200000 characters; edits increment revision and invalidate embeddings transactionally.
- Tags: integer ID, unique case-insensitive name, 1–100 characters. Names are trimmed; SQLite NOCASE provides ASCII case folding, not full Unicode case normalization.
- IdeaTags: normalized many-to-many composite key, cascading association deletion. A note can have up to 40 tags.
- Embeddings: idea FK, revision, chunk ordinal/text, model name/digest, dimension, chunker version and float32 BLOB. Only one active model's chunks are stored per note. Original notes are always authoritative.
- Settings: reserved transactional key/value settings table; current desktop preferences/bootstrap configuration are in settings.json.
- SchemaMigrations: version and applied-at timestamp. PRAGMA user_version is also checked before startup mutates anything.
- IdeasFts / IdeasTrigram: external-content FTS5 tables, synchronized by insert/update/delete triggers. Short queries use literal substring membership to retain punctuation and Chinese terms.

Migration 1 creates the initial schema in a transaction; repeated initialization preserves data. Future schema versions are rejected. Later upgrades must add numbered migrations, never drop/recreate a user's database. Projects do not yet carry timestamps because no V1 behavior consumes them; idea timestamps are preserved.

Archive excludes notes from ordinary search but retains their data and exports. Explicit delete cascades tags/embeddings and removes FTS entries. Renaming tags retains associations. Project/tag deletion does not mutate idea text.

JSON export version 1 preserves project/tag IDs, all notes including archives, relations, content, timestamps and revisions. It excludes rebuildable vectors. Markdown preserves readable content grouped by project. Neither exporter can target the live database or SQLite sidecar files. Raw database backup preserves the complete database, including indexes.

Restore accepts only compatible NovelRAG SQLite databases, validates integrity, expected tables/columns and foreign keys, and keeps `novelrag.db.pre-restore-<timestamp>.bak`. Keep independent backups on another drive for disk-failure protection.
