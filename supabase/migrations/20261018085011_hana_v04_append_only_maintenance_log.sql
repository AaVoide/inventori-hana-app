create table public.hana_maintenance_log (
 id bigint generated always as identity primary key,
 run_id uuid not null,
 recorded_at timestamptz not null default clock_timestamp(),
 phase text not null check(phase in ('APPROVED','STARTED','COMPLETED','CLEANUP_VERIFIED','FAILED')),
 actor text not null check(length(actor) between 1 and 120),
 summary text not null check(length(summary) between 1 and 2000),
 counts jsonb not null default '{}'::jsonb check(jsonb_typeof(counts)='object'),
 retrospective boolean not null default false
);
create index hana_maintenance_run on public.hana_maintenance_log(run_id,id);
alter table public.hana_maintenance_log enable row level security;
revoke all on public.hana_maintenance_log from public,anon,authenticated;
grant select on public.hana_maintenance_log to authenticated;
create policy maintenance_admin_read on public.hana_maintenance_log for select to authenticated using((select hana_private.current_role())='ADMIN');
create function hana_private.keep_maintenance_log() returns trigger language plpgsql security invoker set search_path='' as $$begin raise exception 'Maintenance log is append-only.'; end$$;
revoke all on function hana_private.keep_maintenance_log() from public,anon,authenticated;
create trigger maintenance_no_rewrite before update or delete on public.hana_maintenance_log for each row execute function hana_private.keep_maintenance_log();
