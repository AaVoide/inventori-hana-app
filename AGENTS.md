# Maintenance and test records

Before maintenance or live testing that creates, changes, or removes Supabase records:

1. Inspect the selected project and identify the exact scope. Do not infer blanket permission to delete data from the user's statement that today's data is unimportant.
2. Use one run UUID and append a STARTED entry to `public.hana_maintenance_log` before mutations. Record the relevant authorization when applicable, the target environment, and expected fixture counts. Never log passwords, tokens, private keys, connection strings containing credentials, or raw authentication responses.
3. Keep temporary account/asset IDs in an ignored local fixture manifest; use only that exact scope for cleanup. Do not erase real asset audit history or use broad reset/delete commands.
4. Append COMPLETED or FAILED results, then CLEANUP_VERIFIED only after independent queries confirm removal counts. Preserve the maintenance log when removing temporary asset audit entries. An interrupted run must retain its STARTED record so it can be reconciled.
5. Supply the same UUID as HANA_MAINTENANCE_RUN when running the live test harness. Save a readable report under docs/ with checks, limitations, and cleanup verification, and update the review notes. A missing cloud log is a reporting failure; do not claim a live test is fully recorded until reconciled.

The app's ordinary asset and role audit trail is separate from the Admin-only maintenance log. Existing maintenance records are append-only; corrections are new entries, never edits or deletions. Entries reconstructed after a run must set `retrospective=true` and must not invent event timestamps.

