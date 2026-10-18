using Dapper;
using Microsoft.Data.Sqlite;

namespace InventoryApp.Services;

/// <summary>
/// Writes audit rows. Audit is append-only: there is no update or delete here,
/// and the database refuses both anyway.
/// </summary>
public class AuditService
{
    private const string InsertSql =
        @"INSERT INTO audit_entries
              (entity_type, entity_id, entity_code, action, field_name, old_value, new_value, note, user_id)
          VALUES
              (@entityType, @entityId, @entityCode, @action, @fieldName, @oldValue, @newValue, @note, @userId)";

    /// <summary>Writes one audit row inside an existing transaction.</summary>
    public void Record(
        SqliteConnection conn, SqliteTransaction tx,
        string entityType, long entityId, string? entityCode, string action, long userId,
        string? fieldName = null, string? oldValue = null, string? newValue = null, string? note = null)
    {
        conn.Execute(InsertSql, new
        {
            entityType, entityId, entityCode, action, fieldName, oldValue, newValue, note, userId
        }, tx);
    }

    /// <summary>Writes one standalone audit row (its own connection).</summary>
    public void Record(
        string entityType, long entityId, string? entityCode, string action, long userId,
        string? fieldName = null, string? oldValue = null, string? newValue = null, string? note = null)
    {
        using var conn = Data.Db.Open();
        conn.Execute(InsertSql, new
        {
            entityType, entityId, entityCode, action, fieldName, oldValue, newValue, note, userId
        });
    }
}
