using System.Windows;
using InventoryApp.Data;
using InventoryApp.Models;
using Condition = InventoryApp.Models.Condition;
using InventoryApp.ViewModels;
using InventoryApp.Services;
using System.Windows.Controls;

namespace InventoryApp.Views;

public partial class AddAssetWindow : Window
{
    private readonly MainViewModel _main;
    private static int? _lastBuilding, _lastRoom;
    public static void ClearRememberedLocation() { _lastBuilding=null;_lastRoom=null; }

    public AddAssetWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;

        ItemTypeBox.ItemsSource = new LookupRepository().GetItemTypes();
        BuildingBox.ItemsSource = _main.Buildings;
        BuildingBox.SelectedItem=_main.Buildings.FirstOrDefault(b=>b.Id==_lastBuilding);
        RoomBox.SelectedItem=_main.Rooms.FirstOrDefault(r=>r.Id==_lastRoom && r.BuildingId==_lastBuilding);
        ConditionBox.ItemsSource = _main.Conditions;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Building_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_main is null)return;
        RoomBox.SelectedItem=null;RoomBox.Text="";
        RoomBox.ItemsSource=_main.Rooms.Where(r=>r.BuildingId==(BuildingBox.SelectedItem as Building)?.Id).ToList();
    }
    private void ClearRoom_Click(object sender,RoutedEventArgs e) {RoomBox.SelectedItem=null;RoomBox.Text="";}
    private void NewBuilding_Click(object sender,RoutedEventArgs e)
    {
        try {var name=Forms.Ask(this,"Tambah gedung");if(name is null)return;
        var id=new MasterDataService().Create(MasterKind.Building,name,null,null,Session.User.Id);_main.LoadLookups();BuildingBox.SelectedItem=_main.Buildings.First(b=>b.Id==id);}
        catch(Exception ex){ShowError(ex.Message);}
    }
    private void NewRoom_Click(object sender,RoutedEventArgs e)
    {
        try {if(BuildingBox.SelectedItem is not Building b)throw new ArgumentException("Pilih gedung terlebih dahulu.");
        var name=Forms.Ask(this,"Tambah ruangan di "+b.Name);if(name is null)return;
        var id=new MasterDataService().Create(MasterKind.Room,name,null,b.Id,Session.User.Id);_main.LoadLookups();BuildingBox.SelectedItem=_main.Buildings.First(x=>x.Id==b.Id);RoomBox.SelectedItem=_main.Rooms.First(r=>r.Id==id);}
        catch(Exception ex){ShowError(ex.Message);}
    }
    private void NewItem_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Window {Owner=this};var p=Forms.Body(dialog,"Nama barang baru",480,430);
        var name=Forms.Field(p,"Nama barang",new TextBox {Text=ItemTypeBox.SelectedItem is null?ItemTypeBox.Text:""});
        var service=new MasterDataService();var category=Forms.Field(p,"Kategori",new ComboBox {ItemsSource=service.List(MasterKind.Category).Where(r=>r.State=="Aktif").ToList(),DisplayMemberPath="Name"});
        Forms.Button(p,"+ Kategori baru",()=>{var text=Forms.Ask(dialog,"Tambah kategori");if(text is null)return;var id=service.Create(MasterKind.Category,text,null,null,Session.User.Id);category.ItemsSource=service.List(MasterKind.Category).Where(r=>r.State=="Aktif").ToList();category.SelectedItem=category.Items.Cast<MasterRow>().First(r=>r.Id==id);});
        Forms.Note(p,"Kode aset akan dibuat otomatis ketika barang ditambahkan.");
        Forms.Button(p,"Simpan nama barang",()=>{if(category.SelectedItem is not MasterRow c)throw new ArgumentException("Pilih kategori.");var id=service.Create(MasterKind.ItemType,name.Text,"AST",(int)c.Id,Session.User.Id);ItemTypeBox.ItemsSource=new LookupRepository().GetItemTypes();ItemTypeBox.SelectedItem=ItemTypeBox.Items.Cast<ItemType>().First(i=>i.Id==id);dialog.DialogResult=true;});
        dialog.ShowDialog();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (ItemTypeBox.SelectedItem is not ItemType itemType)
        {
            ShowError("Pilih jenis barang terlebih dahulu.");
            return;
        }

        if (!int.TryParse(QuantityBox.Text.Trim(), out var quantity) || quantity < 1 || quantity > 500)
        {
            ShowError("Jumlah harus berupa angka antara 1 dan 500.");
            return;
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(YearBox.Text))
        {
            if (!int.TryParse(YearBox.Text.Trim(), out var parsed) || parsed < 1900 || parsed > 2100)
            {
                ShowError("Tahun perolehan harus berupa angka empat digit, misalnya 2019.");
                return;
            }
            year = parsed;
        }

        try
        {
            if(BuildingBox.SelectedItem is not Building building)throw new ArgumentException("Pilih gedung dari daftar, atau tambahkan gedung baru.");
            if(RoomBox.SelectedItem is null && !string.IsNullOrWhiteSpace(RoomBox.Text))throw new ArgumentException("Pilih ruangan dari daftar, tambahkan ruangan, atau kosongkan isian.");
            void Save(bool allowDuplicate) => _main.CreateAssets(
                itemType.Id,
                quantity,
                (RoomBox.SelectedItem as Room)?.Id,
                (ConditionBox.SelectedItem as Condition)?.Id,
                year,
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),building.Id,allowDuplicate);
            try {Save(false);}
            catch(DuplicateAssetException duplicate)
            {
                if(new DuplicateWindow(duplicate.Codes){Owner=this}.ShowDialog()!=true)return;
                Save(true);
            }
            _lastBuilding=building.Id;_lastRoom=(RoomBox.SelectedItem as Room)?.Id;
            if(KeepOpen.IsChecked==true){ItemTypeBox.SelectedItem=null;ItemTypeBox.Text="";QuantityBox.Text="1";YearBox.Clear();NotesBox.Clear();ConditionBox.SelectedItem=null;ShowError(_main.StatusMessage);ItemTypeBox.Focus();}
            else Close();
        }
        catch (Exception ex)
        {
            ShowError("Aset gagal dibuat. " + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}


