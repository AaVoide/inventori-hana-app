using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Services;

public enum MasterKind { Category, Building, Room, ItemType, Condition }
// SQLite may report expression columns as BLOB when a query returns no rows.
// Property mapping avoids constructor-type matching against that empty result metadata.
public class MasterRow
{
    public string State { get; set; } = "Aktif";
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Details { get; set; } = "";
}

/// <summary>Small, explicit allowlist: UI text never becomes a SQL table name.</summary>
public class MasterDataService
{
    private static string Table(MasterKind kind) => kind switch
    {
        MasterKind.Category => "categories", MasterKind.Building => "buildings",
        MasterKind.Room => "rooms", MasterKind.ItemType => "item_types",
        MasterKind.Condition => "conditions", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public List<MasterRow> List(MasterKind kind)
    {
        AccessControl.RequireEditor(Session.User.Id);
        using var conn = Db.Open();
        var sql = kind switch
        {
            MasterKind.Room => "SELECT r.id AS Id,r.name AS Name,b.name AS Details,CASE r.is_active WHEN 1 THEN 'Aktif' ELSE 'Tidak aktif' END State FROM rooms r JOIN buildings b ON b.id=r.building_id ORDER BY b.name,r.name",
            MasterKind.ItemType => "SELECT i.id AS Id,i.name AS Name,c.name || ' / ' || i.code_prefix AS Details,CASE i.status WHEN 'ACTIVE' THEN 'Aktif' ELSE 'Tidak aktif' END State FROM item_types i JOIN categories c ON c.id=i.category_id ORDER BY i.name",
            MasterKind.Condition => "SELECT id AS Id,name AS Name,code AS Details,CASE is_active WHEN 1 THEN 'Aktif' ELSE 'Tidak aktif' END State FROM conditions ORDER BY code",
            _ => $"SELECT id AS Id,name AS Name,'' AS Details,CASE is_active WHEN 1 THEN 'Aktif' ELSE 'Tidak aktif' END State FROM {Table(kind)} ORDER BY name"
        };
        return conn.Query<MasterRow>(sql).AsList();
    }

    public long Create(MasterKind kind, string name, string? code, int? parentId, long userId)
    {
        AccessControl.RequireEditor(userId);
        name = name.Trim();
        code = code?.Trim().ToUpperInvariant();
        if (name.Length is < 1 or > 150) throw new ArgumentException("Nama wajib diisi (maksimal 150 karakter).");
        if (kind is MasterKind.Room or MasterKind.ItemType && parentId is null)
            throw new ArgumentException("Pilih gedung atau kategori terlebih dahulu.");
        if (kind is MasterKind.ItemType or MasterKind.Condition &&
            (string.IsNullOrEmpty(code) || code.Length > 12 || !code.All(c => char.IsAsciiLetterOrDigit(c))))
            throw new ArgumentException("Kode harus berisi 1–12 huruf atau angka tanpa spasi.");

        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        if(kind is MasterKind.Room or MasterKind.ItemType)
        {
            var parentTable=kind==MasterKind.Room?"buildings":"categories";
            if(conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM {parentTable} WHERE id=@parentId AND is_active=1",new {parentId},tx)!=1)
                throw new ArgumentException("Pilih gedung atau kategori yang aktif.");
        }
        var table = Table(kind);
        var duplicate = kind switch
        {
            MasterKind.Room => " AND building_id=@parentId",
            MasterKind.ItemType => " AND category_id=@parentId",
            _ => ""
        };
        if (conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE lower(trim(name))=lower(@name){duplicate}",
            new { name, parentId }, tx) > 0)
            throw new ArgumentException("Nama tersebut sudah ada. Pilih data yang tersedia.");

        var sql = kind switch
        {
            MasterKind.Room => "INSERT INTO rooms(name,building_id) VALUES(@name,@parentId)",
            MasterKind.ItemType => "INSERT INTO item_types(name,category_id,code_prefix,created_by) VALUES(@name,@parentId,@code,@userId)",
            MasterKind.Condition => "INSERT INTO conditions(name,code) VALUES(@name,@code)",
            _ => $"INSERT INTO {table}(name) VALUES(@name)"
        };
        conn.Execute(sql, new { name, code, parentId, userId }, tx);
        var id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
        new AuditService().Record(conn, tx, kind.ToString(), id, code, "CREATE", userId,
            newValue: name, note: "Data referensi dibuat dari aplikasi");
        tx.Commit();
        return id;
    }

    public void Change(MasterKind kind,long id,string action,string? name=null)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        using var c=Db.Open(); using var tx=c.BeginTransaction();
        var table=Table(kind);
        var old=c.ExecuteScalar<string>($"SELECT name FROM {table} WHERE id=@id",new {id},tx) ?? throw new ArgumentException("Data tidak ditemukan.");
        if(action=="RENAME")
        {
            name=name?.Trim(); if(string.IsNullOrWhiteSpace(name) || name.Length>150) throw new ArgumentException("Isi nama yang valid.");
            var scope=kind switch {MasterKind.Room=>" AND building_id=(SELECT building_id FROM rooms WHERE id=@id)",MasterKind.ItemType=>" AND category_id=(SELECT category_id FROM item_types WHERE id=@id)",_=>""};
            if(c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE id<>@id AND name=@name COLLATE NOCASE{scope}",new{id,name},tx)>0)throw new ArgumentException("Nama tersebut sudah dipakai.");
            c.Execute($"UPDATE {table} SET name=@name,updated_at=datetime('now','localtime') WHERE id=@id",new {name,id},tx);
        }
        else if(action=="DELETE")
        {
            try { c.Execute($"DELETE FROM {table} WHERE id=@id",new {id},tx); }
            catch(Microsoft.Data.Sqlite.SqliteException ex) when(ex.SqliteErrorCode==19) { throw new ArgumentException("Data masih dipakai. Ubah nama atau nonaktifkan; pindahkan data terkait sebelum menghapus."); }
        }
        else if(action is "DISABLE" or "ENABLE")
        {
            var field=kind==MasterKind.ItemType ? "status" : "is_active";
            object state=kind==MasterKind.ItemType ? (action=="ENABLE"?"ACTIVE":"INACTIVE") : (action=="ENABLE"?1:0);
            c.Execute($"UPDATE {table} SET {field}=@state,updated_at=datetime('now','localtime') WHERE id=@id",new {state,id},tx);
        }
        else throw new ArgumentException("Tindakan tidak dikenal.");
        new AuditService().Record(c,tx,kind.ToString(),id,null,action,Session.User.Id,oldValue:old,newValue:name); tx.Commit();
    }
}
