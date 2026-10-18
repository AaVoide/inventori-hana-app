using System.Windows;
using System.Windows.Input;
using InventoryApp.Services;
using InventoryApp.ViewModels;
using InventoryApp.Views;
using Microsoft.Win32;

namespace InventoryApp;
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    public MainWindow()
    {
        InitializeComponent();DataContext=_vm;
        Loaded+=(_,_)=>{if(GuideWindow.ShouldShow())new GuideWindow {Owner=this}.ShowDialog();};
    }
    private void Keyword_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter)_vm.LoadAssets();}
    private void Cloud_Click(object sender,RoutedEventArgs e)=>Run(()=>new CloudWindow{Owner=this}.ShowDialog());
    private void Refresh(){_vm.LoadLookups();_vm.LoadAssets();}
    private void AddAsset_Click(object sender,RoutedEventArgs e){new AddAssetWindow(_vm){Owner=this}.ShowDialog();Refresh();}
    private void MasterData_Click(object sender,RoutedEventArgs e)=>Run(()=>{new MasterDataWindow{Owner=this}.ShowDialog();Refresh();});
    private void Account_Click(object sender,RoutedEventArgs e)=>Run(()=>new AccountWindow{Owner=this}.ShowDialog());
    private void Password_Click(object sender,RoutedEventArgs e)=>Run(()=>new PasswordWindow(Session.User){Owner=this}.ShowDialog());
    private void Guide_Click(object sender,RoutedEventArgs e)=>Run(()=>new GuideWindow{Owner=this}.ShowDialog());
    private void Import_Click(object sender,RoutedEventArgs e)=>Run(()=>{new ImportWindow{Owner=this}.ShowDialog();Refresh();});
    private void Export_Click(object sender,RoutedEventArgs e)=>Run(()=>{
        var file=new SaveFileDialog {Filter="Excel|*.xlsx|CSV|*.csv",FileName="Inventaris-"+DateTime.Now.ToString("yyyyMMdd"),AddExtension=true};
        if(file.ShowDialog(this)!=true)return;
        SpreadsheetService.Export(file.FileName);MessageBox.Show(this,"Data berhasil diekspor. Berkas ini dapat dibaca tanpa aplikasi dan tidak terenkripsi. Untuk menyimpan akun dan riwayat, gunakan Cadangan aman.","Ekspor selesai");
    });
    private void Backup_Click(object sender,RoutedEventArgs e)=>Run(()=>{
        var password=Forms.Password(this,"Buat cadangan aman",true);if(password is null)return;
        var file=new SaveFileDialog {Filter="Cadangan terenkripsi terkompresi|*.hanaz",FileName="Cadangan-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),AddExtension=true};
        if(file.ShowDialog(this)!=true)return;
        BackupService.Create(file.FileName,password);MessageBox.Show(this,"Cadangan terenkripsi dibuat dan diverifikasi. Untuk memulihkan, buka salinan aplikasi di folder baru, lalu pilih Pulihkan cadangan pada layar awal.","Cadangan selesai");
    });
    private void Logout_Click(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,"Keluar dari akun ini? Perubahan yang belum disimpan akan dibuang.","Ganti akun",MessageBoxButton.YesNo)==MessageBoxResult.Yes)((App)Application.Current).SwitchAccount();
    }
    private void ClearRoom_Click(object sender,RoutedEventArgs e)=>_vm.EditRoom=null;
    private void Exit_Click(object sender,RoutedEventArgs e){if(MessageBox.Show(this,"Tutup aplikasi? Perubahan yang belum disimpan akan dibuang.","Konfirmasi",MessageBoxButton.YesNo)==MessageBoxResult.Yes)Application.Current.Shutdown();}
    private void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Periksa kembali",MessageBoxButton.OK,MessageBoxImage.Warning);}}
}

