using Dapper;
using Microsoft.Data.Sqlite;

namespace InventoryApp.Services;

public sealed class DuplicateAssetException:InvalidOperationException
{
    public IReadOnlyList<string> Codes {get;}
    public DuplicateAssetException(IReadOnlyList<string> codes):base("Aset serupa sudah ada. Periksa kembali sebelum menambah."){Codes=codes;}
}
public static class DuplicateAssets
{
    public static List<string> Find(SqliteConnection c,SqliteTransaction tx,int itemId,int buildingId,int? roomId)
        =>c.Query<string>(@"SELECT a.asset_code FROM assets a JOIN item_types i ON i.id=a.item_type_id
            WHERE lower(trim(i.name))=(SELECT lower(trim(name)) FROM item_types WHERE id=@itemId)
            AND a.building_id=@buildingId AND a.room_id IS @roomId AND a.status='ACTIVE'
            ORDER BY a.asset_code LIMIT 20",new{itemId,buildingId,roomId},tx).AsList();
}
