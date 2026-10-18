-- Reviewed v0.4 schema. One organization's inventory; active registered Viewers can read it.
-- Client writes are restricted to checked, transactional RPCs; no direct table writes.
create schema hana_private;
revoke all on schema hana_private from public, anon;
grant usage on schema hana_private to authenticated;
create table public.hana_profiles (
 id uuid primary key references auth.users(id) on delete cascade,
 email text not null, role text not null default 'VIEWER' check(role in ('ADMIN','EDITOR','VIEWER')),
 active boolean not null default true
);
create table public.hana_assets (
 id uuid primary key, code text not null unique check(length(code) between 1 and 100),
 data jsonb not null check(jsonb_typeof(data)='object'), version bigint not null check(version>0),
 revision bigint generated always as identity unique,
 updated_by uuid not null, updated_at timestamptz not null default now()
);
create table public.hana_audit (
 id bigint generated always as identity primary key, asset_id uuid, actor uuid not null,
 action text not null, before_data jsonb, after_data jsonb, operation_id uuid,
 changed_at timestamptz not null default now()
);
create index hana_audit_asset on public.hana_audit(asset_id,id);
create table hana_private.operations (
 id uuid primary key, actor uuid not null, request jsonb not null, result jsonb not null
);
alter table public.hana_profiles enable row level security;
alter table public.hana_assets enable row level security;
alter table public.hana_audit enable row level security;
alter table hana_private.operations enable row level security;
revoke all on public.hana_profiles,public.hana_assets,public.hana_audit from public,anon,authenticated;
grant select on public.hana_profiles,public.hana_assets,public.hana_audit to authenticated;

create function hana_private.current_role() returns text language sql stable security definer set search_path='' as $$
 select p.role from public.hana_profiles p join auth.users u on u.id=p.id
 where p.id=auth.uid() and p.active and u.email_confirmed_at is not null
 and coalesce((auth.jwt()->>'is_anonymous')::boolean,false)=false
$$;
revoke all on function hana_private.current_role() from public,anon;
grant execute on function hana_private.current_role() to authenticated;
create policy profile_read on public.hana_profiles for select to authenticated
 using(id=(select auth.uid()) or (select hana_private.current_role())='ADMIN');
create policy inventory_read on public.hana_assets for select to authenticated
 using((select hana_private.current_role()) in ('ADMIN','EDITOR','VIEWER'));
create policy audit_read on public.hana_audit for select to authenticated
 using((select hana_private.current_role()) in ('ADMIN','EDITOR','VIEWER'));

create function hana_private.new_user() returns trigger language plpgsql security definer set search_path='' as $$
begin
 insert into public.hana_profiles(id,email,role) values(new.id,coalesce(new.email,''),'VIEWER');
 return new;
end $$;
revoke all on function hana_private.new_user() from public,anon,authenticated;
create trigger hana_signup after insert on auth.users for each row execute function hana_private.new_user();
-- Only the pre-existing, explicitly designated verified administrator is bootstrapped.
insert into public.hana_profiles(id,email,role)
 select id,email,case when lower(email)='ezragustinm@gmail.com' and email_confirmed_at is not null
 and raw_app_meta_data->>'hana_role'='ADMIN' then 'ADMIN' else 'VIEWER' end from auth.users;

create function hana_private.apply_asset(p_operation uuid,p_id uuid,p_expected bigint,p_data jsonb,p_override boolean)
 returns jsonb language plpgsql security definer set search_path='' as $$
declare
 actor uuid:=auth.uid(); role_name text; old public.hana_assets; saved public.hana_assets;
 request jsonb; receipt hana_private.operations; result jsonb; matches jsonb;
 code_value text; action_name text;
begin
 perform pg_advisory_xact_lock(726401);
 role_name:=hana_private.current_role();
 if actor is null or role_name not in ('ADMIN','EDITOR') or role_name is null then raise insufficient_privilege using message='Admin atau Editor diperlukan.'; end if;
 if p_operation is null or p_id is null or p_expected is null or p_expected<0 or p_override is null then raise exception 'Operasi tidak valid.'; end if;
 if p_data is null or jsonb_typeof(p_data)<>'object' or octet_length(p_data::text)>1048576 then raise exception 'Data terlalu besar atau tidak valid.'; end if;
 if length(trim(coalesce(p_data->>'name',''))) not between 1 and 150 or length(trim(coalesce(p_data->>'building',''))) not between 1 and 150 then raise exception 'Nama barang dan gedung wajib.'; end if;
 if coalesce(p_data->>'status','') not in ('ACTIVE','INACTIVE','DISPOSED') then raise exception 'Status tidak valid.'; end if;
 if p_data ? 'acquisition_year' and ((p_data->>'acquisition_year') !~ '^[0-9]{4}$' or (p_data->>'acquisition_year')::int not between 1900 and 2100) then raise exception 'Tahun tidak valid.'; end if;
 -- One ordered writer lock also makes revision cursors safe across transaction commits.
 perform pg_advisory_xact_lock(726401);
 request:=jsonb_build_object('id',p_id,'expected',p_expected,'data',p_data,'override',p_override);
 select * into receipt from hana_private.operations where id=p_operation;
 if found then
   if receipt.actor<>actor or receipt.request<>request then raise exception 'ID operasi sudah dipakai untuk permintaan lain.'; end if;
   return receipt.result;
 end if;
 select * into old from public.hana_assets where id=p_id;
 if (old.id is null and p_expected<>0) or (old.id is not null and old.version<>p_expected) then
   result:=jsonb_build_object('status','CONFLICT','server',case when old.id is null then null else to_jsonb(old) end);
 else
   code_value:=coalesce(nullif(trim(p_data->>'code'),''),'HANA-'||replace(p_id::text,'-',''));
   if old.id is not null and code_value<>old.code then raise exception 'Kode aset permanen.'; end if;
   if old.id is null and exists(select 1 from public.hana_assets where code=code_value) then
     result:=jsonb_build_object('status','CODE_EXISTS');
   else
     select coalesce(jsonb_agg(s.code),'[]') into matches from (
       select code from public.hana_assets where id<>p_id and data->>'status'='ACTIVE'
       and lower(trim(data->>'name'))=lower(trim(p_data->>'name'))
       and lower(trim(data->>'building'))=lower(trim(p_data->>'building'))
       and lower(trim(coalesce(data->>'room','')))=lower(trim(coalesce(p_data->>'room','')))
       order by code limit 20) s;
     if old.id is null and jsonb_array_length(matches)>0 and not p_override then
       result:=jsonb_build_object('status','DUPLICATE','matches',matches);
     else
       if old.id is null and p_data ? 'legacy_history' and role_name<>'ADMIN' then raise insufficient_privilege using message='Transfer riwayat hanya untuk Admin.'; end if;
       if old.id is not null and (p_data->'legacy_history') is distinct from (old.data->'legacy_history') then raise exception 'Riwayat lama tidak boleh diubah.'; end if;
       if old.id is null then
         insert into public.hana_assets(id,code,data,version,updated_by)
         values(p_id,code_value,(p_data-'code')||jsonb_build_object('code',code_value),1,actor) returning * into saved;
         action_name:='CREATE';
       else
         update public.hana_assets set data=(p_data-'code')||jsonb_build_object('code',code_value),version=version+1,
           revision=default,updated_by=actor,updated_at=now() where id=p_id returning * into saved;
         action_name:='UPDATE';
       end if;
       insert into public.hana_audit(asset_id,actor,action,before_data,after_data,operation_id)
         values(p_id,actor,action_name,old.data,saved.data,p_operation);
       if old.id is null and jsonb_array_length(matches)>0 then
         insert into public.hana_audit(asset_id,actor,action,after_data,operation_id)
         values(p_id,actor,'DUPLICATE_OVERRIDE',jsonb_build_object('matches',matches),p_operation);
       end if;
       result:=jsonb_build_object('status','APPLIED','server',to_jsonb(saved));
     end if;
   end if;
 end if;
 insert into hana_private.operations values(p_operation,actor,request,result);
 return result;
end $$;
revoke all on function hana_private.apply_asset(uuid,uuid,bigint,jsonb,boolean) from public,anon;
grant execute on function hana_private.apply_asset(uuid,uuid,bigint,jsonb,boolean) to authenticated;
create function public.hana_apply_asset(p_operation uuid,p_id uuid,p_expected bigint,p_data jsonb,p_override boolean default false)
 returns jsonb language sql security invoker set search_path='' as $$
 select hana_private.apply_asset(p_operation,p_id,p_expected,p_data,p_override)
$$;
revoke all on function public.hana_apply_asset(uuid,uuid,bigint,jsonb,boolean) from public,anon;
grant execute on function public.hana_apply_asset(uuid,uuid,bigint,jsonb,boolean) to authenticated;

create function hana_private.set_role(p_user uuid,p_role text,p_active boolean) returns void
 language plpgsql security definer set search_path='' as $$
declare old public.hana_profiles;
begin
 perform pg_advisory_xact_lock(726401);
 if auth.uid() is null or hana_private.current_role() is distinct from 'ADMIN' then raise insufficient_privilege; end if;
 if p_role is null or p_role not in ('ADMIN','EDITOR','VIEWER') or p_active is null then raise exception 'Peran tidak valid.'; end if;
 select * into strict old from public.hana_profiles where id=p_user;
 if old.role='ADMIN' and old.active and (p_role<>'ADMIN' or not p_active)
 and (select count(*) from public.hana_profiles where role='ADMIN' and active)<=1 then raise exception 'Admin terakhir harus tetap aktif.'; end if;
 update public.hana_profiles set role=p_role,active=p_active where id=p_user;
 insert into public.hana_audit(actor,action,before_data,after_data)
 values(auth.uid(),'ROLE_CHANGE',to_jsonb(old),jsonb_build_object('id',p_user,'role',p_role,'active',p_active));
end $$;
revoke all on function hana_private.set_role(uuid,text,boolean) from public,anon;
grant execute on function hana_private.set_role(uuid,text,boolean) to authenticated;
create function public.hana_set_role(p_user uuid,p_role text,p_active boolean) returns void
 language sql security invoker set search_path='' as $$select hana_private.set_role(p_user,p_role,p_active)$$;
revoke all on function public.hana_set_role(uuid,text,boolean) from public,anon;
grant execute on function public.hana_set_role(uuid,text,boolean) to authenticated;
