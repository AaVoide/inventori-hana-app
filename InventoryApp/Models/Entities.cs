namespace InventoryApp.Models;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "USER";
    public bool IsActive { get; set; }
    public string ActiveLabel => IsActive?"Ya":"Tidak";
    public string RoleLabel => Role switch {"ADMIN"=>"Admin","EDITOR"=>"Editor","VIEWER"=>"Viewer",_=>"Perlu ditinjau"};
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ItemType
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? TypeDescription { get; set; }
    public string CodePrefix { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
    public string CategoryName { get; set; } = "";

    /// <summary>Shown in dropdowns: "Mebel / Kursi Chitose".</summary>
    public string Display => $"{CategoryName} / {Name}";
}

public class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Room
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public string Name { get; set; } = "";
    public string BuildingName { get; set; } = "";

    /// <summary>Shown in dropdowns: "Gedung A / Ruang 101".</summary>
    public string Display => $"{BuildingName} / {Name}";
}

public class Condition
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Display => $"{Code} - {Name}";
}

/// <summary>One row of the asset list (read from v_asset_list).</summary>
public class AssetListItem
{
    public long RowVersion { get; set; }
    public long Id { get; set; }
    public string AssetCode { get; set; } = "";
    public string ItemTypeName { get; set; } = "";
    public string? TypeDescription { get; set; }
    public string CategoryName { get; set; } = "";
    public string? BuildingName { get; set; }
    public string? RoomName { get; set; }
    public int? RoomId { get; set; }
    public int? BuildingId { get; set; }
    public string? ConditionCode { get; set; }
    public string? ConditionName { get; set; }
    public int? ConditionId { get; set; }
    public string Status { get; set; } = "";
    public int? AcquisitionYear { get; set; }
    public string? Notes { get; set; }
    public string? LegacyCode { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string LocationDisplay =>
        BuildingName is null ? "Gedung perlu diisi" : RoomName is null ? BuildingName : $"{BuildingName} / {RoomName}";
}

public class AuditEntry
{
    public long Id { get; set; }
    public string EntityType { get; set; } = "";
    public long EntityId { get; set; }
    public string? EntityCode { get; set; }
    public string Action { get; set; } = "";
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Note { get; set; }
    public string UserName { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

/// <summary>Values the user may change on an asset. Asset code is absent on purpose.</summary>
public class AssetChangeSet
{
    public int? BuildingId { get; set; }
    public long ExpectedVersion { get; set; }
    public int? RoomId { get; set; }
    public int? ConditionId { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public int? AcquisitionYear { get; set; }
    public string? Notes { get; set; }
}
