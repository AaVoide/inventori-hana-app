using System.Text;
using Dapper;
using InventoryApp.Models;

namespace InventoryApp.Data;

public class AssetRepository
{
    private static (string Sql, DynamicParameters Parameters) Filter(string? keyword, int? buildingId, string? status)
    {
        var sql = new StringBuilder(" FROM v_asset_list v WHERE 1=1");
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql.Append(@" AND (AssetCode LIKE @kw ESCAPE '\' OR ItemTypeName LIKE @kw ESCAPE '\'
                OR LegacyCode LIKE @kw ESCAPE '\' OR RoomName LIKE @kw ESCAPE '\'
                OR EXISTS (SELECT 1 FROM identifiers i WHERE i.asset_id=v.Id AND i.is_active=1 AND i.value=@identifier))");
            var escaped = keyword.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            p.Add("kw", $"%{escaped}%");
            p.Add("identifier", keyword.Trim());
        }
        if (buildingId is not null) { sql.Append(" AND BuildingId=@buildingId"); p.Add("buildingId", buildingId); }
        if (!string.IsNullOrWhiteSpace(status)) { sql.Append(" AND Status=@status"); p.Add("status", status); }
        return (sql.ToString(), p);
    }

    public List<AssetListItem> Search(string? keyword, int? buildingId, string? status, int page = 0, int pageSize = 100)
    {
        if (page < 0 || pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(page));
        var (sql, p) = Filter(keyword, buildingId, status);
        p.Add("limit", pageSize); p.Add("offset", checked(page * pageSize));
        using var conn = Db.Open();
        return conn.Query<AssetListItem>("SELECT v.*" + sql + " ORDER BY AssetCode,Id LIMIT @limit OFFSET @offset", p).AsList();
    }

    public int Count(string? keyword, int? buildingId, string? status)
    {
        var (sql, p) = Filter(keyword, buildingId, status);
        using var conn = Db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*)" + sql, p);
    }

    public AssetListItem? FindByIdentifier(string value)
    {
        using var conn = Db.Open();
        return conn.QuerySingleOrDefault<AssetListItem>(@"SELECT v.* FROM v_asset_list v
            WHERE v.AssetCode=@value OR EXISTS (SELECT 1 FROM identifiers i
            WHERE i.asset_id=v.Id AND i.is_active=1 AND i.value=@value) LIMIT 1", new { value });
    }

    public List<AuditEntry> GetHistory(long assetId)
    {
        using var conn = Db.Open();
        return conn.Query<AuditEntry>(@"SELECT a.id AS Id,a.entity_type AS EntityType,a.entity_id AS EntityId,
            a.entity_code AS EntityCode,a.action AS Action,a.field_name AS FieldName,
            a.old_value AS OldValue,a.new_value AS NewValue,a.note AS Note,
            u.full_name AS UserName,a.changed_at AS ChangedAt
            FROM audit_entries a JOIN users u ON u.id=a.user_id
            WHERE a.entity_type='Asset' AND a.entity_id=@assetId
            ORDER BY a.changed_at DESC,a.id DESC", new { assetId }).AsList();
    }
}
