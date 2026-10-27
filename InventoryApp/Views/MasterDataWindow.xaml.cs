using System.Windows;
using System.Windows.Controls;
using InventoryApp.Services;

namespace InventoryApp.Views;

public partial class MasterDataWindow : Window
{
    private readonly MasterDataService _service = new();
    public MasterDataWindow()
    {
        AccessControl.RequireAdmin(Session.User.Id);
        InitializeComponent();
        Refresh();
    }
    private MasterKind Kind => (MasterKind)KindBox.SelectedIndex;
    private void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Rows is not null) Refresh();
    }
    private void Refresh()
    {
        Rows.ItemsSource = _service.List(Kind);
        NameBox.Clear(); CodeBox.Clear(); MessageText.Text = "";
        ParentPanel.Visibility = Kind is MasterKind.Room or MasterKind.ItemType ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility = Kind is MasterKind.ItemType or MasterKind.Condition ? Visibility.Visible : Visibility.Collapsed;
        ParentLabel.Text = Kind == MasterKind.Room ? "Gedung" : "Kategori";
        CodeLabel.Text = Kind == MasterKind.ItemType ? "Awalan kode aset (contoh: KRS)" : "Kode kondisi resmi";
        ParentBox.ItemsSource = Kind is MasterKind.Room or MasterKind.ItemType
            ? _service.List(Kind == MasterKind.Room ? MasterKind.Building : MasterKind.Category).Where(r=>r.State=="Aktif").ToList() : null;
        if(Kind==MasterKind.ItemType)CodeBox.Text="AST";
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int? parent = ParentBox.SelectedItem is MasterRow row ? checked((int)row.Id) : null;
            _service.Create(Kind, NameBox.Text, CodeBox.Text, parent, Session.User.Id);
            Refresh();
            MessageText.Text = "Data berhasil ditambahkan.";
        }
        catch (Exception ex) { MessageText.Text = "Gagal menambah data. " + ex.Message; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Change(string action)
    {
        try
        {
            if(Rows.SelectedItem is not MasterRow row)throw new ArgumentException("Pilih satu baris terlebih dahulu.");
            string? name=null;
            if(action=="RENAME"){name=Forms.Ask(this,"Ganti nama",row.Name);if(name is null)return;}
            if(action=="TOGGLE")action=row.State=="Aktif"?"DISABLE":"ENABLE";
            if(action!="RENAME" && MessageBox.Show(this,$"{action switch {"DELETE"=>"Hapus","DISABLE"=>"Nonaktifkan",_=>"Aktifkan"}} {row.Name}?", "Konfirmasi",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            _service.Change(Kind,row.Id,action,name);Refresh();MessageText.Text="Perubahan disimpan.";
        }
        catch(Exception ex){MessageText.Text=ex.Message;}
    }
    private void Rename_Click(object s,RoutedEventArgs e)=>Change("RENAME");
    private void Toggle_Click(object s,RoutedEventArgs e)=>Change("TOGGLE");
    private void Delete_Click(object s,RoutedEventArgs e)=>Change("DELETE");
}
