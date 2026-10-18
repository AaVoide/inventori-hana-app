# Maintenance and live-test log

The permanent cloud record is `public.hana_maintenance_log`, visible to active Admins only. The review app provides **Log pemeliharaan**. App accounts cannot insert, edit, or delete these maintenance records; trusted maintenance tooling appends them. The asset/role audit is separate.

## Run 7b988890-4564-4330-b9d2-36f0e30eec9d — 2026-10-18

Scope: two explicitly approved temporary Auth accounts and their test records in inventori-hana. The approval, results, and cleanup entries were recorded retrospectively after this run, with that fact marked in the database. Their recorded_at values are recording times, not reconstructed execution times.

- User authorized creation and later removal of two temporary accounts.
- 13 real HTTP checks passed, using the actual client and queue/sync implementation.
- Two clients on the development computer verified shared persistence and stale-edit conflicts. This does not substitute for acceptance testing on two physical computers.
- Simulated disconnection retained the encrypted queue; a simulated lost acknowledgement after an actual server commit retried to exactly one asset and one audit record.
- One initial network timeout resolved on retry.
- Cleanup removed two temporary accounts and three test assets, plus their identities, sessions, audit fixtures and operation receipts.
- Independent queries confirmed zero remaining fixture accounts, identities, sessions, assets, audit records, or receipts. The real Admin remains Admin.
- Generated temporary credentials were never printed and their protected local fixture file was removed.

Future runs must record STARTED before changes, results afterward, and cleanup verification separately under the same run UUID. See AGENTS.md. Passwords and tokens must never be included.
