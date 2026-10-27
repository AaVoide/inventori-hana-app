using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Services;

public static class AccessControl
{
    public static void RequireUser(long userId)
    {
        if (Session.CurrentUser?.Id != userId)
            throw new UnauthorizedAccessException("Silakan masuk kembali.");
        using var conn = Db.Open();
        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM users WHERE id=@userId AND is_active=1", new { userId }) != 1)
            throw new UnauthorizedAccessException("Akun tidak aktif.");
    }

    public static void RequireAdmin(long userId)
    {
        RequireUser(userId);
        using var conn = Db.Open();
        if (conn.ExecuteScalar<string>("SELECT role FROM user_access WHERE user_id=@userId", new { userId }) != "ADMIN")
            throw new UnauthorizedAccessException("Hanya admin yang dapat melakukan tindakan ini.");
    }
    public static void RequireEditor(long userId)
    {
        RequireUser(userId);
        using var conn=Db.Open();
        if(conn.ExecuteScalar<string>("SELECT role FROM user_access WHERE user_id=@userId",new{userId}) is not ("ADMIN" or "EDITOR"))
            throw new UnauthorizedAccessException("Hanya Admin atau Editor yang dapat mengubah inventaris.");
    }
}
