using System.Windows;
using InventoryApp.Services;

namespace InventoryApp.Views;

public partial class SetupWindow : Window
{
    public SetupWindow() { InitializeComponent(); }
    private void RestoreAutomatic_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var file=new Microsoft.Win32.OpenFileDialog {Filter="Cadangan otomatis|*.hanaz",InitialDirectory=AutomaticBackup.Folder};
            if(file.ShowDialog(this)!=true)return;
            var password=AutomaticBackup.RecoveryPasswordFromFolder(System.IO.Path.GetDirectoryName(file.FileName)!);
            BackupService.RestoreFresh(file.FileName,password);DialogResult=true;
        }
        catch(Exception ex){ErrorText.Text="Pemulihan otomatis gagal. Gunakan akun Windows yang sama dan folder dengan recovery.key. "+ex.Message;}
    }
    private void Restore_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var file=new Microsoft.Win32.OpenFileDialog {Filter="Cadangan Inventaris Aset|*.hanaz;*.hana"};
            if(file.ShowDialog(this)!=true)return;
            var password=Forms.Password(this,"Pulihkan cadangan",false);if(password is null)return;
            BackupService.RestoreFresh(file.FileName,password);DialogResult=true;
        }
        catch(Exception ex){ErrorText.Text="Pemulihan gagal. Periksa berkas dan kata sandi. "+ex.Message;}
    }
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PasswordBox.Password != ConfirmBox.Password)
                throw new ArgumentException("Kata sandi dan pengulangannya berbeda.");
            new SetupService().CreateAdministrator(UsernameBox.Text, FullNameBox.Text, PasswordBox.Password);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}

