using Dapper;
using InventoryApp.Models;

namespace InventoryApp.Data;

/// <summary>Master data used to fill dropdowns.</summary>
public class LookupRepository
{
    public List<Building> GetBuildings()
    {
        using var conn = Db.Open();
        return conn.Query<Building>(
            "SELECT id AS Id, name AS Name FROM buildings WHERE is_active = 1 ORDER BY name").AsList();
    }

    public List<Room> GetRooms()
    {
        using var conn = Db.Open();
        return conn.Query<Room>(
            @"SELECT r.id AS Id, r.building_id AS BuildingId, r.name AS Name, b.name AS BuildingName
              FROM rooms r JOIN buildings b ON b.id = r.building_id
              WHERE r.is_active = 1 AND b.is_active=1
              ORDER BY b.name, r.name").AsList();
    }

    public List<Condition> GetConditions()
    {
        using var conn = Db.Open();
        return conn.Query<Condition>(
            @"SELECT id AS Id, code AS Code, name AS Name
              FROM conditions WHERE is_active = 1 ORDER BY sort_order, code").AsList();
    }

    public List<ItemType> GetItemTypes()
    {
        using var conn = Db.Open();
        return conn.Query<ItemType>(
            @"SELECT it.id AS Id, it.category_id AS CategoryId, it.name AS Name,
                     it.type_description AS TypeDescription, it.code_prefix AS CodePrefix,
                     it.status AS Status, c.name AS CategoryName
              FROM item_types it JOIN categories c ON c.id = it.category_id
              WHERE it.status = 'ACTIVE' AND c.is_active=1
              ORDER BY c.name, it.name").AsList();
    }

    public string GetSetting(string key, string fallback)
    {
        using var conn = Db.Open();
        return conn.QuerySingleOrDefault<string>(
            "SELECT setting_value FROM app_settings WHERE setting_key = @key", new { key }) ?? fallback;
    }
}
