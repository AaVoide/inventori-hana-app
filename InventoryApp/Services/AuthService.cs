using InventoryApp.Data;
using InventoryApp.Models;

namespace InventoryApp.Services;

public class AuthService
{
    private readonly UserRepository _users = new();
    private readonly AuditService _audit = new();

    /// <summary>Returns the user on success, or null when the credentials do not match.</summary>
    public User? Login(string username, string password)
    {
        var user = _users.FindByUsername(username);
        if (user is null || !user.IsActive) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;
        if (user.Role == "REVIEW") throw new InvalidOperationException("Admin perlu menentukan peran akun lama ini di Kelola akun.");

        _users.TouchLastLogin(user.Id);
        _audit.Record("User", user.Id, user.Username, "LOGIN", user.Id, note: "Login aplikasi");
        return user;
    }
}
