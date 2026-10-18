using InventoryApp.Models;

namespace InventoryApp;

/// <summary>The user currently logged in. Set once by the login window.</summary>
public static class Session
{
    public static User? CurrentUser { get; set; }

    public static User User =>
        CurrentUser ?? throw new InvalidOperationException("Belum ada pengguna yang login.");

    public static bool IsAdmin => CurrentUser?.Role == "ADMIN";
    public static bool CanEdit => CurrentUser?.Role is "ADMIN" or "EDITOR";
}
