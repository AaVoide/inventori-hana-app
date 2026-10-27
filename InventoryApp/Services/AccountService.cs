using Dapper;
using InventoryApp.Data;
using InventoryApp.Models;

namespace InventoryApp.Services;

public class AccountService
{
    public List<User> List()
    {
        AccessControl.RequireAdmin(Session.User.Id);
        using var conn=Db.Open();
        return conn.Query<User>(@"SELECT u.id Id,username Username,full_name FullName,is_active IsActive,
            COALESCE(a.role,'REVIEW') Role FROM users u LEFT JOIN user_access a ON a.user_id=u.id ORDER BY username").AsList();
    }
    public void Create(string username,string name,string password,string role)
    {
        AccessControl.RequireAdmin(Session.User.Id); ValidateRole(role); ValidatePassword(password);
        username=username.Trim(); name=name.Trim();
        if(username.Length is <3 or >50 || name.Length is <1 or >100) throw new ArgumentException("Isi nama lengkap dan nama pengguna (3–50 karakter).");
        using var c=Db.Open(); using var tx=c.BeginTransaction();
        if(c.ExecuteScalar<int>("SELECT COUNT(*) FROM users WHERE lower(username)=lower(@username)",new {username},tx)>0) throw new ArgumentException("Nama pengguna sudah dipakai.");
        c.Execute("INSERT INTO users(username,full_name,password_hash,role) VALUES(@username,@name,@hash,'USER')",new {username,name,hash=PasswordHasher.Hash(password)},tx);
        var id=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
        c.Execute("INSERT INTO user_access VALUES(@id,@role)",new {id,role},tx);
        new AuditService().Record(c,tx,"User",id,username,"CREATE",Session.User.Id,newValue:role);
        tx.Commit();
    }
    public void SetAccess(long id,string role,bool active)
    {
        AccessControl.RequireAdmin(Session.User.Id); ValidateRole(role);
        using var c=Db.Open(); using var tx=c.BeginTransaction();
        var old=c.QuerySingle<User>("SELECT u.id Id,username Username,is_active IsActive,a.role Role FROM users u JOIN user_access a ON a.user_id=u.id WHERE u.id=@id",new {id},tx);
        if(id==Session.User.Id && (role!="ADMIN" || !active)) throw new ArgumentException("Tidak dapat menonaktifkan atau menurunkan akses akun sendiri.");
        if(old.Role=="ADMIN" && old.IsActive && (role!="ADMIN" || !active) &&
            c.ExecuteScalar<int>("SELECT COUNT(*) FROM users u JOIN user_access a ON a.user_id=u.id WHERE a.role='ADMIN' AND u.is_active=1",transaction:tx)<=1)
            throw new ArgumentException("Harus ada minimal satu admin aktif.");
        c.Execute("UPDATE user_access SET role=@role WHERE user_id=@id; UPDATE users SET is_active=@active,updated_at=datetime('now','localtime') WHERE id=@id",new {id,role,active},tx);
        new AuditService().Record(c,tx,"User",id,old.Username,"UPDATE",Session.User.Id,oldValue:$"{old.Role}/{old.IsActive}",newValue:$"{role}/{active}"); tx.Commit();
    }
    public void ChangePassword(long id,string oldPassword,string newPassword)
    {
        AccessControl.RequireUser(Session.User.Id); ValidatePassword(newPassword);
        if(id!=Session.User.Id) AccessControl.RequireAdmin(Session.User.Id);
        using var c=Db.Open(); using var tx=c.BeginTransaction();
        var hash=c.ExecuteScalar<string>("SELECT password_hash FROM users WHERE id=@id",new {id},tx) ?? throw new ArgumentException("Akun tidak ditemukan.");
        if(id==Session.User.Id && !PasswordHasher.Verify(oldPassword,hash)) throw new ArgumentException("Kata sandi lama salah.");
        c.Execute("UPDATE users SET password_hash=@hash,updated_at=datetime('now','localtime') WHERE id=@id",new {id,hash=PasswordHasher.Hash(newPassword)},tx);
        new AuditService().Record(c,tx,"User",id,null,"PASSWORD_CHANGE",Session.User.Id,note:"Kata sandi diubah"); tx.Commit();
    }
    private static void ValidateRole(string role) { if(role is not ("ADMIN" or "EDITOR" or "VIEWER")) throw new ArgumentException("Pilih Admin, Editor, atau Viewer."); }
    private static void ValidatePassword(string value) { if(value.Length<10) throw new ArgumentException("Kata sandi minimal 10 karakter."); }
}
