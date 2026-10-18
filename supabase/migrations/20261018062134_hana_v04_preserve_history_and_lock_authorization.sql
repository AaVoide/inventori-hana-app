create or replace function hana_private.apply_asset(p_operation uuid,p_id uuid,p_expected bigint,p_data jsonb,p_override boolean)
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

create policy operations_client_deny on hana_private.operations for all to authenticated using(false) with check(false);
