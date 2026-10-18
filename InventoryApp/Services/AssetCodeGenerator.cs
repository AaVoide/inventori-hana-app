using Dapper;
using Microsoft.Data.Sqlite;

namespace InventoryApp.Services;

/// <summary>
/// Builds the permanent asset code. The exact format is not final yet, so it lives
/// in one place: change BuildCode and nothing else in the app has to change.
/// Current shape: PREFIX-0001, taken from the item type prefix plus a per-prefix counter.
/// Location deliberately plays no part, because an asset can move.
/// </summary>
public static class AssetCodeGenerator
{
    /// <summary>
    /// Reserves <paramref name="count"/> numbers for a prefix and returns the codes.
    /// Must run inside a transaction.
    /// </summary>
    public static List<string> Reserve(
        SqliteConnection conn, SqliteTransaction tx, string prefix, int count, int padding = 4)
    {
        prefix = prefix.Trim().ToUpperInvariant();

        conn.Execute(
            @"INSERT INTO asset_code_sequences (prefix, next_number) VALUES (@prefix, 1)
              ON CONFLICT(prefix) DO NOTHING",
            new { prefix }, tx);

        var next = conn.QuerySingle<long>(
            "SELECT next_number FROM asset_code_sequences WHERE prefix = @prefix",
            new { prefix }, tx);

        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
            codes.Add(BuildCode(prefix, checked(next + i), padding));

        conn.Execute(
            "UPDATE asset_code_sequences SET next_number = @next WHERE prefix = @prefix",
            new { prefix, next = next + count }, tx);

        return codes;
    }

    private static string BuildCode(string prefix, long number, int padding)
        => $"{prefix}-{number.ToString().PadLeft(padding, '0')}";
}
