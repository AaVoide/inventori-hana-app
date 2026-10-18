using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using InventoryApp.Services;

namespace InventoryApp.Views;

public class ImportWindow:Window
{
    private readonly ComboBox _sheet=new(),_delimiter=new() {ItemsSource=new[]{"Koma ( , )","Titik koma ( ; )","Tab"},SelectedIndex=0};
    private readonly WrapPanel _mapping=new();
    private readonly Expander _mappingPanel=new() {Header="Pemetaan kolom — klik untuk melihat / mengubah",Margin=new Thickness(0,10,0,0)};
    private readonly DataGrid _grid=new() {AutoGenerateColumns=false,IsReadOnly=false,CanUserAddRows=false,CanUserDeleteRows=false,Height=270};
    private readonly CheckBox _new=new() {Content="Izinkan pembuatan kategori, barang, gedung, dan ruangan baru",Margin=new Thickness(0,8,0,0)};
    private readonly CheckBox _similar=new() {Content="Saya sudah memeriksa dan mengizinkan baris yang mirip",Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock _status;
    private readonly List<ComboBox> _columns=new();
    private string _path="",_hash="";
    private string _loadedSheet="";
    private TextBlock _ignored;
    private SheetData? _data;
    private List<ImportRow> _rows=new();
    public ImportWindow(string? initialFile=null)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        var p=Forms.Body(this,"Buka Excel / CSV",1150,850);
        Forms.Note(p,"Impor menambah aset baru. Berkas asli tidak diubah. Baris pertama harus berisi judul kolom. Pilih kolom, lalu periksa semua baris sebelum menyimpan. Maksimal 10.000 aset per impor.");
        Forms.Button(p,"1. Pilih berkas .xlsx / .csv",Choose);
        var row=new WrapPanel();p.Children.Add(row);
        _sheet.Width=260;_delimiter.Width=200;_sheet.Margin=new Thickness(0,0,16,0);
        Forms.Field(row,"Lembar kerja: ",_sheet);Forms.Field(row,"Pemisah CSV: ",_delimiter);
        Forms.Button(p,"2. Baca lembar dan cocokkan kolom",Read);
        _mappingPanel.Content=_mapping;p.Children.Add(_mappingPanel);
        Forms.Button(p,"3. Tampilkan / buat ulang pratinjau",Preview);
        _ignored=Forms.Note(p,"");
        Forms.Button(p,"Isi gedung untuk semua baris yang masih kosong",()=>{if(_rows.Count==0)throw new ArgumentException("Tampilkan pratinjau terlebih dahulu.");var value=Forms.Ask(this,"Gedung untuk baris kosong");if(value is null)return;foreach(var r in _rows.Where(r=>string.IsNullOrWhiteSpace(r.Building)))r.Building=value;_grid.Items.Refresh();Validate();});
        Forms.Note(p,"Pratinjau dapat diedit. Kolom Masalah menjelaskan apa yang perlu diperbaiki. Jumlah kosong tidak otomatis ditebak; tanpa kolom jumlah, setiap baris dihitung satu barang.");
        _grid.Columns.Add(new DataGridTextColumn {Header="Baris asal",Binding=new Binding("SourceRow"),IsReadOnly=true,Width=75});
        var problemStyle=new Style(typeof(TextBlock));
        problemStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,System.Windows.Media.Brushes.DarkRed));
        problemStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
        problemStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty,new Binding("Problem")));
        _grid.Columns.Add(new DataGridTextColumn {Header="Masalah (arahkan kursor untuk detail)",Binding=new Binding("Problem"),IsReadOnly=true,Width=240,ElementStyle=problemStyle});
        _grid.FrozenColumnCount=2;
        for(var i=0;i<SpreadsheetService.Fields.Length;i++)_grid.Columns.Add(new DataGridTextColumn {Header=SpreadsheetService.Labels[i],Binding=new Binding(SpreadsheetService.Fields[i]) {UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=130});
        p.Children.Add(_grid);p.Children.Add(_new);p.Children.Add(_similar);
        _status=Forms.Note(p,"Belum ada berkas dipilih.");
        var actions=new WrapPanel();p.Children.Add(actions);
        Forms.Button(actions,"Periksa data",()=>Validate());
        Forms.Button(actions,"4. Simpan impor",Save);
        Forms.Button(actions,"Tutup",Close);
        if(initialFile is not null){LoadFile(initialFile);Preview();}
    }
    private void Choose()
    {
        var dialog=new OpenFileDialog {Filter="Excel / CSV|*.xlsx;*.csv"};if(dialog.ShowDialog(this)!=true)return;
        LoadFile(dialog.FileName);
    }
    private void LoadFile(string path)
    {
        if(new FileInfo(path).Length>30_000_000)throw new ArgumentException("Maksimal 30 MB.");
        var sheets=SpreadsheetService.Sheets(path);
        _path=path;_sheet.ItemsSource=sheets;_sheet.SelectedIndex=0;
        _data=null;_rows.Clear();_grid.ItemsSource=null;_mapping.Children.Clear();_columns.Clear();
        _status.Text=Path.GetFileName(_path);Read();
    }
    private void Read()
    {
        if(_path=="" || _sheet.SelectedItem is not string sheet)throw new ArgumentException("Pilih berkas dan lembar.");
        var before=SpreadsheetService.Hash(_path);
        _data=SpreadsheetService.Read(_path,sheet,new[]{",",";","\t"}[_delimiter.SelectedIndex]);
        _hash=SpreadsheetService.Hash(_path);if(_hash!=before)throw new IOException("Berkas berubah saat dibaca. Baca ulang.");
        _loadedSheet=sheet;
        _mappingPanel.IsExpanded=true;
        _mapping.Children.Clear();_columns.Clear();_rows.Clear();_grid.ItemsSource=null;
        foreach(var field in SpreadsheetService.Fields)
        {
            var panel=new StackPanel {Width=230,Margin=new Thickness(0,0,12,8)};_mapping.Children.Add(panel);
            var combo=new ComboBox {ItemsSource=new[]{"— Tidak dipakai —"}.Concat(_data.Headers.Select((h,i)=>$"{i+1}. {h}")).ToArray(),SelectedIndex=SpreadsheetService.Guess(field,_data.Headers)+1};
            Forms.Field(panel,SpreadsheetService.Labels[Array.IndexOf(SpreadsheetService.Fields,field)],combo);_columns.Add(combo);
        }
        _status.Text=$"{Path.GetFileName(_path)} • {sheet} • {_data.Rows.Count} baris. Periksa pemetaan kolom.";
    }
    private void Preview()
    {
        if(_data is null)throw new ArgumentException("Baca berkas terlebih dahulu.");
        if(_rows.Count>0 && MessageBox.Show(this,"Buat ulang pratinjau? Perubahan manual pada pratinjau akan dibuang.","Pratinjau",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var map=_columns.Select(c=>c.SelectedIndex-1).ToArray();
        if(map.Where(i=>i>=0).GroupBy(i=>i).Any(g=>g.Count()>1))throw new ArgumentException("Satu kolom sumber hanya boleh dipakai untuk satu isian.");
        var ignored=_data.Headers.Where((h,i)=>!map.Contains(i)).ToArray();
        _ignored.Text=ignored.Length>0?"Kolom yang tidak diimpor: "+string.Join(", ",ignored)+". Periksa sebelum menyimpan. Kode aset baru dibuat otomatis; status awal Aktif.":"Semua kolom sumber dipetakan.";
        _rows=SpreadsheetService.Map(_data,map);_grid.ItemsSource=_rows;_mappingPanel.IsExpanded=false;Validate();
    }
    private bool Validate()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
        var valid=SpreadsheetService.Validate(_rows,_new.IsChecked==true,_similar.IsChecked==true);
        _grid.Items.Refresh();var count=_rows.Sum(r=>int.TryParse(r.Quantity,out var n)?(long)n:0);
        _status.Text=$"{_rows.Count} baris → {count} aset • {_rows.Count(r=>r.Problem!="")} baris bermasalah. "+(valid?"Siap disimpan.":"Perbaiki isian, lalu periksa lagi.");return valid;
    }
    private void Save()
    {
        if(!Validate())return;
        if(SpreadsheetService.Hash(_path)!=_hash)throw new IOException("Berkas sumber berubah. Baca dan periksa ulang sebelum impor.");
        if(_similar.IsChecked==true && new DuplicateWindow(_rows.Take(20).Select(r=>$"Baris {r.SourceRow}: {r.Name}")){Owner=this}.ShowDialog()!=true)return;
        var total=_rows.Sum(r=>int.Parse(r.Quantity));
        if(MessageBox.Show(this,$"Tambahkan {total} aset dari {_rows.Count} baris? Setiap aset mendapat kode baru. Impor ini tidak memperbarui aset lama.","Konfirmasi impor",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var count=SpreadsheetService.Import(_rows,Path.GetFileName(_path)+" / "+_loadedSheet,_hash+":"+_loadedSheet,_new.IsChecked==true,_similar.IsChecked==true);
        MessageBox.Show(this,$"{count} aset berhasil diimpor.","Impor selesai");DialogResult=true;
    }
}
