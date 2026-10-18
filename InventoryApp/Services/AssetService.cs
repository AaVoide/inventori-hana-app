using Dapper;
using Microsoft.Data.Sqlite;
using InventoryApp.Data;
using InventoryApp.Models;

namespace InventoryApp.Services;

/// <summary>
/// Everything that changes asset data. Each operation runs in one transaction
/// so the asset row and its audit rows are written together or not at all.
/// </summary>
public class AssetService
{
    private readonly AuditService _audit = new();
    private readonly LookupRepository _lookups = new();

    /// <summary>
    /// Creates <paramref name="quantity"/> physical assets for one item type.
    /// This is the rule that makes quantity 23 in the old spreadsheet become 23 rows here.
    /// Returns the codes that were assigned.
    /// </summary>
    public List<string> CreateAssets(
        int itemTypePrefixSourceId, int quantity, int? roomId, int? conditionId,
        int? acquisitionYear, string? notes, long userId, int? buildingId = null,bool duplicateOverride=false)
    {
        AccessControl.RequireEditor(userId);
        if (quantity is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(quantity), "Jumlah harus antara 1 dan 500.");
        ValidateYear(acquisitionYear);

        var padding = int.TryParse(_lookups.GetSetting("asset_code_seq_padding", "4"), out var p) ? p : 4;

        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        ValidateReferences(conn, tx, roomId, conditionId);
        buildingId ??= roomId is null ? null : conn.ExecuteScalar<int?>("SELECT building_id FROM rooms WHERE id=@roomId",new {roomId},tx);
        ValidateBuilding(conn,tx,buildingId,roomId);
        var similar=DuplicateAssets.Find(conn,tx,itemTypePrefixSourceId,buildingId!.Value,roomId);
        if(similar.Count>0 && !duplicateOverride)throw new DuplicateAssetException(similar);
        var prefix = conn.QuerySingleOrDefault<string>(
            "SELECT code_prefix FROM item_types WHERE id = @id AND status='ACTIVE' AND category_id IN (SELECT id FROM categories WHERE is_active=1)", new { id = itemTypePrefixSourceId }, tx)
            ?? throw new InvalidOperationException("Jenis barang tidak ditemukan.");

        var codes = AssetCodeGenerator.Reserve(conn, tx, prefix, quantity, padding);
        if(similar.Count>0)_audit.Record(conn,tx,"AssetBatch",0,codes[0],"DUPLICATE_OVERRIDE",userId,
            note:$"Tetap Tambah: {quantity} barang fisik. Contoh kode serupa: {string.Join(", ",similar)}");

        foreach (var code in codes)
        {
            conn.Execute(
                @"INSERT INTO assets
                      (asset_code, item_type_id, building_id, room_id, condition_id, status,
                       acquisition_year, notes, created_by, updated_by)
                  VALUES
                      (@code, @itemTypeId, @buildingId, @roomId, @conditionId, 'ACTIVE',
                       @acquisitionYear, @notes, @userId, @userId)",
                new
                {
                    code, itemTypeId = itemTypePrefixSourceId, buildingId, roomId, conditionId,
                    acquisitionYear, notes, userId
                }, tx);

            var assetId = conn.QuerySingle<long>("SELECT last_insert_rowid()", transaction: tx);

            // Every asset gets a QR value equal to its code. The label encodes the id only,
            // never the whole record.
            conn.Execute(
                "INSERT INTO identifiers (asset_id, identifier_type, value) VALUES (@assetId, 'QR', @code)",
                new { assetId, code }, tx);

            _audit.Record(conn, tx, "Asset", assetId, code, "CREATE", userId,
                note: "Aset dibuat dari aplikasi");
        }

        tx.Commit();
        return codes;
    }

    /// <summary>
    /// Saves user-editable fields. The asset code is never touched here; the database
    /// rejects a change to it as well. Only fields that really changed get audit rows.
    /// </summary>
    public int UpdateAsset(long assetId, AssetChangeSet changes, long userId)
    {
        AccessControl.RequireEditor(userId);
        ValidateYear(changes.AcquisitionYear);
        if (changes.Status is not ("ACTIVE" or "INACTIVE" or "DISPOSED"))
            throw new ArgumentException("Status aset tidak valid.");
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();

        var current = conn.QuerySingleOrDefault<CurrentAsset>(
            @"SELECT asset_code AS AssetCode, building_id AS BuildingId, room_id AS RoomId, condition_id AS ConditionId,
                     status AS Status, acquisition_year AS AcquisitionYear, notes AS Notes, row_version AS RowVersion
              FROM assets WHERE id = @assetId",
            new { assetId }, tx)
            ?? throw new InvalidOperationException("Aset tidak ditemukan.");

        ValidateReferences(conn,tx,changes.RoomId==current.RoomId?null:changes.RoomId,changes.ConditionId==current.ConditionId?null:changes.ConditionId);
        ValidateBuilding(conn,tx,changes.BuildingId,changes.RoomId,changes.BuildingId==current.BuildingId);

        if (current.RowVersion != changes.ExpectedVersion)
            throw new InvalidOperationException("Aset telah diubah sejak dibuka. Cari ulang aset lalu periksa perubahan sebelum menyimpan.");

        var changed = 0;

        void Compare(string field, string? oldText, string? newText, string action)
        {
            if (string.Equals(oldText ?? "", newText ?? "", StringComparison.Ordinal)) return;
            _audit.Record(conn, tx, "Asset", assetId, current.AssetCode, action, userId,
                fieldName: field, oldValue: oldText, newValue: newText);
            changed++;
        }

        Compare("building_id", Label(conn, tx, "building", current.BuildingId), Label(conn, tx, "building", changes.BuildingId), "MOVE");
        Compare("room_id", Label(conn, tx, "room", current.RoomId), Label(conn, tx, "room", changes.RoomId), "MOVE");
        Compare("condition_id", Label(conn, tx, "condition", current.ConditionId), Label(conn, tx, "condition", changes.ConditionId), "UPDATE");
        Compare("status", current.Status, changes.Status, "STATUS_CHANGE");
        Compare("acquisition_year", current.AcquisitionYear?.ToString(), changes.AcquisitionYear?.ToString(), "UPDATE");
        Compare("notes", current.Notes, changes.Notes, "UPDATE");

        if (changed == 0)
        {
            tx.Rollback();
            return 0;
        }

        conn.Execute(
            @"UPDATE assets
              SET building_id=@BuildingId, room_id = @RoomId, condition_id = @ConditionId, status = @Status,
                  acquisition_year = @AcquisitionYear, notes = @Notes, updated_by = @userId,
                  updated_at = strftime('%Y-%m-%d %H:%M:%f','now','localtime'), row_version = row_version + 1
              WHERE id = @assetId",
            new
            {
                changes.BuildingId, changes.RoomId, changes.ConditionId, changes.Status,
                changes.AcquisitionYear, changes.Notes, userId, assetId
            }, tx);

        tx.Commit();
        return changed;
    }

    /// <summary>Turns an id into readable text so the audit trail stays legible years later.</summary>
    private static string? Label(SqliteConnection conn, SqliteTransaction tx,
        string kind, int? id)
    {
        if (id is null) return null;

        return kind switch
        {
            "building" => conn.QuerySingleOrDefault<string>("SELECT name FROM buildings WHERE id=@id",new {id},tx),
            "room" => conn.QuerySingleOrDefault<string>(
                @"SELECT (b.name || ' / ' || r.name) FROM rooms r
                  JOIN buildings b ON b.id = r.building_id WHERE r.id = @id", new { id }, tx),
            "condition" => conn.QuerySingleOrDefault<string>(
                "SELECT (code || ' - ' || name) FROM conditions WHERE id = @id", new { id }, tx),
            _ => id.ToString()
        };
    }

    private class CurrentAsset
    {
        public int? BuildingId { get; set; }
        public long RowVersion { get; set; }
        public string AssetCode { get; set; } = "";
        public int? RoomId { get; set; }
        public int? ConditionId { get; set; }
        public string Status { get; set; } = "";
        public int? AcquisitionYear { get; set; }
        public string? Notes { get; set; }
    }

    private static void ValidateYear(int? year)
    {
        if (year is < 1900 or > 2100) throw new ArgumentException("Tahun perolehan harus antara 1900 dan 2100.");
    }

    private static void ValidateBuilding(SqliteConnection conn, SqliteTransaction tx, int? buildingId, int? roomId,bool allowInactive=false)
    {
        if (buildingId is null || conn.ExecuteScalar<int>("SELECT COUNT(*) FROM buildings WHERE id=@buildingId AND (is_active=1 OR @allowInactive=1)",new {buildingId,allowInactive},tx)!=1)
            throw new ArgumentException("Pilih gedung penyimpanan.");
        if(roomId is not null && conn.ExecuteScalar<int>("SELECT COUNT(*) FROM rooms WHERE id=@roomId AND building_id=@buildingId",new {roomId,buildingId},tx)!=1)
            throw new ArgumentException("Ruangan tidak berada di gedung pilihan.");
    }

    private static void ValidateReferences(SqliteConnection conn, SqliteTransaction tx, int? roomId, int? conditionId)
    {
        if (roomId is not null && conn.ExecuteScalar<int>("SELECT COUNT(*) FROM rooms WHERE id=@roomId AND is_active=1", new { roomId }, tx) != 1)
            throw new ArgumentException("Ruangan tidak tersedia.");
        if (conditionId is not null && conn.ExecuteScalar<int>("SELECT COUNT(*) FROM conditions WHERE id=@conditionId AND is_active=1", new { conditionId }, tx) != 1)
            throw new ArgumentException("Kondisi tidak tersedia.");
    }
}
