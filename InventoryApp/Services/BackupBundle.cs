using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace InventoryApp.Services;

/// <summary>Versioned, authenticated encrypted ZIP containing a SQLCipher snapshot.
/// Temporary database files remain encrypted; neither password nor key is logged.</summary>
public static class BackupBundle
{
    private static readonly byte[] Magic=Encoding.ASCII.GetBytes("HANA040\n");
    private const int MaximumBytes=256*1024*1024;
    private const int Iterations=210_000;
    private sealed record Manifest(int Format,int Schema,string SnapshotKey);

    public static void Create(SqliteConnection source,string destination,string password)
    {
        if(password.Length<12)throw new ArgumentException("Kata sandi cadangan minimal 12 karakter.");
        destination=Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var snapshot=destination+".snapshot-"+Guid.NewGuid().ToString("N");
        var staging=destination+".writing-"+Guid.NewGuid().ToString("N");
        byte[]? packed=null,key=null;
        try
        {
            var snapshotKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            DatabaseVault.Export(source,snapshot,snapshotKey);
            if(new FileInfo(snapshot).Length>MaximumBytes-1_048_576)throw new InvalidOperationException("Cadangan melebihi batas 255 MB untuk versi ulasan ini.");
            using(var memory=new MemoryStream())
            {
                using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
                {
                    using(var entry=zip.CreateEntry("inventory.db",CompressionLevel.SmallestSize).Open())
                    using(var file=File.OpenRead(snapshot))file.CopyTo(entry);
                    using var manifest=zip.CreateEntry("manifest.json",CompressionLevel.SmallestSize).Open();
                    JsonSerializer.Serialize(manifest,new Manifest(1,source.ExecuteScalar<int>("PRAGMA user_version"),snapshotKey));
                }
                packed=memory.ToArray();
            }
            var salt=RandomNumberGenerator.GetBytes(16);var nonce=RandomNumberGenerator.GetBytes(12);
            key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
            var encrypted=new byte[packed.Length];var tag=new byte[16];
            using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,packed,encrypted,tag,Magic);
            using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {output.Write(Magic);output.Write(salt);output.Write(nonce);output.Write(tag);output.Write(encrypted);output.Flush(true);}
            // Read/authenticate/decompress and check SQLite before replacing any previous backup.
            Verify(staging,password);
            File.Move(staging,destination,true);
        }
        finally
        {
            if(key is not null)CryptographicOperations.ZeroMemory(key);
            if(packed is not null)CryptographicOperations.ZeroMemory(packed);
            if(File.Exists(snapshot))File.Delete(snapshot);
            if(File.Exists(staging))File.Delete(staging);
        }
    }

    public static void Verify(string path,string password)=>ReadSnapshot(path,password,source=>
    {
        if(source.ExecuteScalar<string>("PRAGMA integrity_check")!="ok" || source.Query("PRAGMA foreign_key_check").Any())
            throw new InvalidOperationException("Integritas cadangan tidak valid.");
    });

    public static void RestoreTo(string path,string password,string destination,string destinationKey)
    {
        if(File.Exists(destination))throw new IOException("Tujuan pemulihan harus berupa berkas baru.");
        try { ReadSnapshot(path,password,source=>DatabaseVault.Export(source,destination,destinationKey)); }
        catch { if(File.Exists(destination))File.Delete(destination);throw; }
    }

    private static void ReadSnapshot(string path,string password,Action<SqliteConnection> action)
    {
        var size=new FileInfo(path).Length;
        if(size<52 || size>MaximumBytes+52)throw new InvalidDataException("Ukuran cadangan tidak valid.");
        var data=File.ReadAllBytes(path);
        if(!data.AsSpan(0,8).SequenceEqual(Magic))throw new InvalidDataException("Format cadangan tidak dikenal.");
        var key=Rfc2898DeriveBytes.Pbkdf2(password,data.AsSpan(8,16),Iterations,HashAlgorithmName.SHA256,32);
        var packed=new byte[data.Length-52];
        var snapshot=Path.Combine(Path.GetTempPath(),"hana-recovery-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            try {using var aes=new AesGcm(key,16);aes.Decrypt(data.AsSpan(24,12),data.AsSpan(52),data.AsSpan(36,16),packed,Magic);}
            catch(AuthenticationTagMismatchException){throw new InvalidDataException("Kata sandi salah atau berkas cadangan rusak.");}
            using var memory=new MemoryStream(packed,false);using var zip=new ZipArchive(memory,ZipArchiveMode.Read);
            if(zip.Entries.Count!=2 || zip.Entries.Count(e=>e.FullName=="inventory.db")!=1 || zip.Entries.Count(e=>e.FullName=="manifest.json")!=1)
                throw new InvalidDataException("Isi cadangan tidak valid.");
            var manifestEntry=zip.GetEntry("manifest.json")!;var database=zip.GetEntry("inventory.db")!;
            if(manifestEntry.Length>4096 || database.Length>MaximumBytes)throw new InvalidDataException("Isi cadangan terlalu besar.");
            using var manifestStream=manifestEntry.Open();var manifest=JsonSerializer.Deserialize<Manifest>(manifestStream)??throw new InvalidDataException("Manifest hilang.");
            if(manifest.Format!=1 || manifest.Schema is <2 or >3)throw new InvalidDataException("Versi cadangan tidak didukung.");
            using(var input=database.Open())using(var output=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var buffer=new byte[81920];long written=0;int length;
                while((length=input.Read(buffer))>0){written+=length;if(written>MaximumBytes)throw new InvalidDataException("Isi cadangan terlalu besar.");output.Write(buffer,0,length);}
            }
            using var source=DatabaseVault.Open(snapshot,manifest.SnapshotKey);
            if(source.ExecuteScalar<int>("PRAGMA user_version")!=manifest.Schema || source.ExecuteScalar<string>("PRAGMA integrity_check")!="ok" || source.Query("PRAGMA foreign_key_check").Any())
                throw new InvalidDataException("Database cadangan tidak valid.");
            action(source);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(packed);
            if(File.Exists(snapshot))File.Delete(snapshot);
        }
    }
}
