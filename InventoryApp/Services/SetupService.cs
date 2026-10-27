using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Services;

public class SetupService
{
    public bool NeedsSetup()
    {
        using var conn = Db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM users") == 0;
    }

    public void CreateAdministrator(string username, string fullName, string password)
    {
        username = username.Trim();
        fullName = fullName.Trim();
        if (username.Length is < 3 or > 50 || fullName.Length is < 1 or > 100 || password.Length < 10)
            throw new ArgumentException("Isi nama lengkap, nama pengguna (3–50 karakter), dan kata sandi minimal 10 karakter.");
        var hash = PasswordHasher.Hash(password);
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM users", transaction: tx) != 0)
            throw new InvalidOperationException("Akun awal sudah dibuat. Silakan masuk.");
        conn.Execute("INSERT INTO users(username,full_name,password_hash,role) VALUES(@username,@fullName,@hash,'ADMIN')",
            new { username, fullName, hash }, tx);
        var id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
        conn.Execute("INSERT INTO user_access(user_id,role) VALUES(@id,'ADMIN')",new { id },tx);
        new AuditService().Record(conn, tx, "User", id, username, "CREATE", id, note: "Akun admin awal dibuat");
        tx.Commit();
    }
}
