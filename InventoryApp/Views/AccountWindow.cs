using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using InventoryApp.Models;
using InventoryApp.Services;

namespace InventoryApp.Views;

public class AccountWindow:Window
{
    public AccountWindow()
    {
        AccessControl.RequireAdmin(Session.User.Id);
        var service=new AccountService();var p=Forms.Body(this,"Kelola akun",760,760);
        Forms.Note(p,"Admin mengelola akun dan inventaris. Editor mengedit inventaris. Viewer hanya melihat. Akun lama dengan status Perlu ditinjau harus dipilih perannya sebelum dapat masuk.");
        var grid=new DataGrid {Height=200,IsReadOnly=true,AutoGenerateColumns=false};
        foreach(var column in new[]{("Pengguna","Username"),("Nama","FullName"),("Peran","RoleLabel"),("Aktif","ActiveLabel")})
            grid.Columns.Add(new DataGridTextColumn {Header=column.Item1,Binding=new Binding(column.Item2),Width=column.Item2=="FullName"?220:140,MinWidth=100});
        p.Children.Add(grid);
        var role=Forms.Field(p,"Peran akun terpilih",new ComboBox {ItemsSource=new[]{"ADMIN","EDITOR","VIEWER"},SelectedIndex=2});
        var active=new CheckBox {Content="Akun aktif",IsChecked=true,Margin=new Thickness(0,10,0,0)};p.Children.Add(active);
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is User u){role.SelectedItem=u.Role;active.IsChecked=u.IsActive;}};
        void Refresh()=>grid.ItemsSource=service.List();
        Forms.Button(p,"Simpan akses akun terpilih",()=>{if(grid.SelectedItem is not User u) throw new ArgumentException("Pilih akun.");service.SetAccess(u.Id,role.SelectedItem as string??"",active.IsChecked==true);Refresh();});
        Forms.Button(p,"Atur ulang kata sandi akun terpilih",()=>{if(grid.SelectedItem is not User u) throw new ArgumentException("Pilih akun.");new PasswordWindow(u) {Owner=this}.ShowDialog();});
        var createPanel=new StackPanel();p.Children.Add(new Expander {Header="+ Tambah akun baru",Content=createPanel,Margin=new Thickness(0,20,0,0)});p=createPanel;
        var newRole=Forms.Field(p,"Peran akun baru",new ComboBox {ItemsSource=new[]{"ADMIN","EDITOR","VIEWER"},SelectedIndex=2});
        var username=Forms.Field(p,"Nama pengguna",new TextBox());var name=Forms.Field(p,"Nama lengkap",new TextBox());
        var password=Forms.Field(p,"Kata sandi (minimal 10 karakter)",new PasswordBox());
        var confirm=Forms.Field(p,"Ulangi kata sandi",new PasswordBox());
        Forms.Button(p,"Buat akun",()=>{if(password.Password!=confirm.Password) throw new ArgumentException("Kedua kata sandi harus sama.");service.Create(username.Text,name.Text,password.Password,newRole.SelectedItem as string??"");username.Clear();name.Clear();password.Clear();confirm.Clear();Refresh();MessageBox.Show("Akun berhasil dibuat.");});
        Refresh();
    }
}
public class PasswordWindow:Window
{
    public PasswordWindow(User user)
    {
        var p=Forms.Body(this,"Kata sandi — "+user.Username,480,480);
        var old=user.Id==Session.User.Id?Forms.Field(p,"Kata sandi saat ini",new PasswordBox()):null;
        var next=Forms.Field(p,"Kata sandi baru (minimal 10 karakter)",new PasswordBox());
        var confirm=Forms.Field(p,"Ulangi kata sandi baru",new PasswordBox());
        Forms.Button(p,"Simpan kata sandi",()=>{if(next.Password!=confirm.Password)throw new ArgumentException("Kedua kata sandi harus sama.");new AccountService().ChangePassword(user.Id,old?.Password??"",next.Password);DialogResult=true;});
    }
}

