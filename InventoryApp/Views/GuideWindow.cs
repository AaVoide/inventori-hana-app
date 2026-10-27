using System.Windows;
using System.Windows.Controls;
using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Views;
public class GuideWindow:Window
{
    public static bool ShouldShow()
    {
        using var c=Db.Open();return c.ExecuteScalar<int>("SELECT COALESCE((SELECT hide_guide FROM ui_preferences WHERE user_id=@id),0)",new{id=Session.User.Id})==0;
    }
    public GuideWindow()
    {
        var p=Forms.Body(this,"Mulai menggunakan Inventaris Aset",620,650);
        Forms.Note(p,Session.IsAdmin?
            "1. Siapkan lokasi\nBuka Data referensi untuk menambah gedung. Ruangan boleh ditambahkan nanti. Kategori membantu mengelompokkan barang.\n\n2. Catat barang\nKlik Tambah aset → pilih nama barang → gedung wajib → ruangan opsional → jumlah. Nama barang baru dapat dibuat langsung. Setiap barang fisik mendapat kode permanen sendiri.\n\n3. Atau buka spreadsheet\nKlik Buka Excel / CSV, cocokkan kolom, periksa dan perbaiki pratinjau. Data baru disimpan setelah Anda mengonfirmasi impor. Header harus di baris pertama.\n\n4. Cari dan perbarui\nCari nama atau kode, pilih baris, lalu simpan perubahan. Kode tidak berubah saat lokasi dipindahkan. Riwayat tetap tersimpan.\n\n5. Simpan salinan\nEkspor Excel / CSV menghasilkan data yang dapat dibaca. Cadangan aman menyimpan seluruh database, akun, dan riwayat dengan kata sandi pemulihan.\n\n6. Kelola akses\nTambahkan Admin atau Viewer di Kelola akun. Gunakan Ganti akun untuk logout. Viewer hanya dapat melihat data."
            :"1. Cari nama atau kode aset.\n\n2. Gunakan penyaring gedung dan status untuk mempersempit hasil.\n\n3. Pilih aset untuk melihat lokasi, kondisi, dan riwayatnya.\n\n4. Akun Viewer hanya dapat melihat. Hubungi Admin untuk perubahan atau ekspor.\n\n5. Gunakan Ganti akun setelah selesai memakai komputer bersama.");
        Forms.Note(p,"Versi ini digunakan di satu komputer. Kode aset disiapkan untuk QR; pencetakan label dan akses melalui Wi-Fi belum tersedia.");
        var hide=new CheckBox {Content="Jangan tampilkan otomatis lagi",IsChecked=!ShouldShow(),Margin=new Thickness(0,12,0,4)};p.Children.Add(hide);
        Forms.Button(p,"Mulai",()=>{using var c=Db.Open();c.Execute("INSERT INTO ui_preferences(user_id,hide_guide) VALUES(@id,@hide) ON CONFLICT(user_id) DO UPDATE SET hide_guide=@hide",new{id=Session.User.Id,hide=hide.IsChecked==true});Close();});
    }
}
