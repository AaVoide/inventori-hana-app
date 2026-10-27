using Dapper;
using InventoryApp.Models;

namespace InventoryApp.Data;

public class UserRepository
{
    public User? FindByUsername(string username)
    {
        using var conn = Db.Open();
        return conn.QuerySingleOrDefault<User>(
            @"SELECT id AS Id, username AS Username, full_name AS FullName,
                     password_hash AS PasswordHash, COALESCE((SELECT role FROM user_access WHERE user_id=users.id),'REVIEW') AS Role, is_active AS IsActive
              FROM users WHERE username = @username",
            new { username });
    }

    public void TouchLastLogin(long userId)
    {
        using var conn = Db.Open();
        conn.Execute("UPDATE users SET last_login_at = datetime('now', 'localtime') WHERE id = @userId", new { userId });
    }
}
