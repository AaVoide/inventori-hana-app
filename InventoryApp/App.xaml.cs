using System.Windows;
using InventoryApp.Views;
using InventoryApp.Services;

namespace InventoryApp;

public partial class App : Application
{
    private bool _databaseReady;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var needsSetup=new SetupService().NeedsSetup();_databaseReady=true;
            SaveSessionBackup();
            MainWindow = new CloudWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
            if(DatabaseVault.LegacyCopy is not null)
                MessageBox.Show("Database aktif sekarang terenkripsi. Salinan skema sebelum pembaruan juga terenkripsi di:\n"+DatabaseVault.LegacyCopy+"\n\nSalinan ini memerlukan kunci lokal yang sama. Buat Cadangan aman untuk pemulihan di komputer lain. Database di folder aplikasi lama dan ekspor lama tidak diubah.","Migrasi database");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aplikasi tidak dapat dibuka.\n\n" + ex.Message,
                "Inventaris Aset", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    public void OpenLocal()
    {
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        MainWindow?.Close();
        if(new SetupService().NeedsSetup() && new SetupWindow().ShowDialog()!=true){Shutdown();return;}
        if(new LoginWindow().ShowDialog()!=true){Shutdown();return;}
        MainWindow=new MainWindow();ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();
    }
    public void SwitchAccount()
    {
        SaveSessionBackup();
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        MainWindow?.Close();Session.CurrentUser=null;AddAssetWindow.ClearRememberedLocation();
        if(new LoginWindow().ShowDialog()!=true){Shutdown();return;}
        MainWindow=new MainWindow();ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SaveSessionBackup();base.OnExit(e);
    }
    private void SaveSessionBackup()
    {
        if(!_databaseReady)return;
        try{AutomaticBackup.SaveChangedSession();}
        catch(Exception ex){MessageBox.Show("Cadangan otomatis belum berhasil. Data utama tetap tersimpan. Hubungi Admin.\n\n"+ex.Message,"Cadangan otomatis");}
    }
}
