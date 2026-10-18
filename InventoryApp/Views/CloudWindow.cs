using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dapper;
using InventoryApp.Data;
using InventoryApp.Services;
using Microsoft.Win32;

namespace InventoryApp.Views;

public sealed class CloudWindow:Window
{
    private readonly CloudClient _api=new();
    private readonly CloudStore _store=new();
    private readonly CloudSync _sync;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly DockPanel _root=new(){Margin=new Thickness(20)};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    private readonly DataGrid _grid=new(){IsReadOnly=true,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Single,MinHeight=180};
    private readonly TextBox _search=new(){Width=260};
    private readonly TextBox _name=new(),_category=new(),_building=new(),_room=new(),_condition=new(),_notes=new(),_year=new();
    private readonly ComboBox _state=new(){ItemsSource=new[]{"ACTIVE","INACTIVE","DISPOSED"},SelectedIndex=0};
    private readonly TextBlock _code=new(){TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel _editor=new();
    private CloudAsset? _selected;
    private bool _opened,_closed,_busy;
    public CloudWindow()
    {
        _sync=new(_api,_store);
        Title="Inventori HANA v0.4 · Tinjauan cloud";Width=1240;Height=820;MinWidth=960;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/InventoryApp;component/Assets/App.ico"));
        Content=_root;
        _timer.Tick+=async(_,_)=>{if(_opened)await SyncAsync();};
        Closed+=(_,_)=>{_closed=true;_timer.Stop();};
        LoginView();
    }
    private static Button Button(string title,Action action)
    {
        var b=new Button{Content=title,Margin=new Thickness(0,4,8,4),Padding=new Thickness(10,6,10,6)};
        b.Click+=(_,_)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Periksa kembali");}};return b;
    }
    private void LoginView()
    {
        Detach(_status);
        _root.Children.Clear();var p=new StackPanel{Width=500,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20)};
        _root.Children.Add(new ScrollViewer{Content=p});
        Forms.Note(p,"Inventori HANA v0.4 · Build tinjauan").FontSize=26;
        Forms.Note(p,"Cloud menyimpan inventaris bersama antar komputer. Masuk dengan email Supabase. Akun lokal lama tetap tersedia melalui Inventaris lokal.");
        var email=Forms.Field(p,"Email",new TextBox{Text=_api.Session?.Profile.Email??""});
        var password=Forms.Field(p,"Kata sandi",new PasswordBox());
        var login=Button("Masuk cloud",async()=>
        {
            if(_busy)return;_busy=true;
            try{await _api.LoginAsync(email.Text.Trim(),password.Password);password.Clear();OpenInventory();await SyncAsync();}
            catch(Exception ex){_status.Text=ex.Message;}finally{_busy=false;}
        });p.Children.Add(login);
        p.Children.Add(Button("Daftar sebagai Viewer",async()=>
        {
            if(_busy)return;_busy=true;
            try{await _api.RegisterAsync(email.Text.Trim(),password.Password);password.Clear();_status.Text="Pendaftaran dikirim. Periksa email konfirmasi, lalu masuk. Akun baru selalu Viewer.";}
            catch(Exception ex){_status.Text=ex.Message;}finally{_busy=false;}
        }));
        if(_api.Session is not null)
        {
            p.Children.Add(Button("Lanjutkan akun tersimpan",async()=>{OpenInventory();await SyncAsync();}));
            p.Children.Add(Button("Buka cache offline",()=>{OpenInventory();_status.Text="Offline · izin berdasarkan login terakhir; perubahan akan diperiksa server saat tersambung.";}));
        }
        p.Children.Add(Button("Inventaris lokal / Excel & referensi",()=>((App)Application.Current).OpenLocal()));
        p.Children.Add(Button("Pulihkan cadangan ke lokasi data kosong",()=>
        {
            var file=new OpenFileDialog{Filter="Cadangan HANA|*.hanaz;*.hana"};if(file.ShowDialog(this)!=true)return;
            var secret=Forms.Password(this,"Pulihkan cadangan",false);if(secret is null)return;
            BackupService.RestoreFresh(file.FileName,secret);_status.Text="Cadangan dipulihkan. Masuk cloud untuk mengakses cache dan antrean; gunakan login lokal untuk data lokal.";
        }));
        p.Children.Add(Button("Hubungi Admin",ContactAdmin));
        p.Children.Add(_status);
        Forms.Note(p,"Untuk uji dua komputer: gunakan akun cloud yang sama di keduanya, atau minta Admin memberikan akses Editor. Data cloud memakai gedung wajib dan ruangan opsional.");
    }
    private void OpenInventory()
    {
        if(_api.Session is null)throw new UnauthorizedAccessException("Masuk terlebih dahulu.");
        Detach(_status);Detach(_grid);Detach(_editor);Detach(_search);
        _opened=true;_root.Children.Clear();_editor.Children.Clear();
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);_root.Children.Add(top);
        Forms.Note(top,"Inventori HANA · Cloud · "+_api.Session.Profile.Email).FontSize=22;
        var actions=new WrapPanel();top.Children.Add(actions);
        actions.Children.Add(Button("Sinkron sekarang",async()=>await SyncAsync()));
        actions.Children.Add(Button("Tambah aset",NewAsset));
        actions.Children.Add(Button("Antrean / konflik",ShowQueue));
        actions.Children.Add(Button("Riwayat aset",async()=>{try{if(_selected is not null)ShowText("Riwayat server",await _api.AuditAsync(_selected.Id));}catch(Exception ex){_status.Text=ex.Message;}}));
        actions.Children.Add(Button("Kelola akun",ShowAccounts));
        actions.Children.Add(Button("Transfer inventaris lokal",TransferLocal));
        actions.Children.Add(Button("Cadangan aman",Backup));
        actions.Children.Add(Button("Ekspor CSV",ExportCsv));
        actions.Children.Add(Button("Rilis GitHub",ReviewUpdates.Open));
        actions.Children.Add(Button("Hubungi Admin",ContactAdmin));
        actions.Children.Add(Button("Keluar cloud",async()=>
        {
            if(MessageBox.Show(this,"Keluar dari cloud? Antrean tetap disimpan untuk akun pembuatnya.","Keluar",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            _opened=false;_timer.Stop();
            try{await _api.LogoutAsync();}catch{ /* Local tokens are cleared even when offline. */ }
            LoginView();
        }));
        top.Children.Add(_status);
        var filters=new WrapPanel();filters.Children.Add(_search);filters.Children.Add(Button("Cari nama / kode / lokasi",Reload));top.Children.Add(filters);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});_root.Children.Add(body);
        _grid.Columns.Clear();
        foreach(var (label,path,width) in new[]{("Kode permanen","Code",280),("Nama barang","Name",180),("Lokasi","Location",180),("Sinkronisasi","State",120)})
            _grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new System.Windows.Data.Binding(path),Width=width,MinWidth=100});
        body.Children.Add(_grid);var scroll=new ScrollViewer{Content=_editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(16,0,0,0)};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        Forms.Note(_editor,"Detail aset").FontSize=20;
        _editor.Children.Add(_code);
        Forms.Field(_editor,"Nama barang *",_name);Forms.Field(_editor,"Kategori",_category);
        Forms.Field(_editor,"Gedung *",_building);Forms.Field(_editor,"Ruangan (opsional)",_room);
        Forms.Field(_editor,"Kondisi",_condition);Forms.Field(_editor,"Status",_state);
        Forms.Field(_editor,"Tahun perolehan",_year);_notes.AcceptsReturn=true;_notes.Height=65;Forms.Field(_editor,"Catatan",_notes);
        _editor.Children.Add(Button("Simpan dan sinkron",Save));
        Forms.Note(_editor,"Simpan bekerja saat offline. Jika aset sudah diubah dari komputer lain, pilih Antrean / konflik untuk membandingkan kedua versi.");
        _grid.SelectionChanged-=SelectionChanged;_grid.SelectionChanged+=SelectionChanged;
        Reload();NewAsset();_timer.Start();_ = CheckUpdatesAsync();
    }
    private static void Detach(FrameworkElement element)
    {
        if(element.Parent is Panel panel)panel.Children.Remove(element);
        else if(element.Parent is Decorator decorator)decorator.Child=null;
        else if(element.Parent is ContentControl content)content.Content=null;
    }
    private async Task CheckUpdatesAsync()
    {
        try{var result=await ReviewUpdates.CheckAsync();if(!_closed&&_opened)Title="Inventori HANA v0.4 · "+result;}
        catch(System.Net.Http.HttpRequestException){ /* Availability of updates never blocks inventory. */ }
        catch(TaskCanceledException){ }
    }
    private void SelectionChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_grid.SelectedItem is not CloudAsset a)return;
        _selected=CloudJson.Read<CloudAsset>(CloudJson.Write(a));Fill();
    }
    private void Fill()
    {
        var a=_selected!;_code.Text=a.Code;_name.Text=a.Data.Name;_category.Text=a.Data.Category;
        _building.Text=a.Data.Building;_room.Text=a.Data.Room;_condition.Text=a.Data.Condition;
        _state.SelectedItem=a.Data.Status;_notes.Text=a.Data.Notes;_year.Text=a.Data.AcquisitionYear?.ToString()??"";
        _editor.IsEnabled=_api.CanEdit;
    }
    private void NewAsset()
    {
        var id=Guid.NewGuid();var code="HANA-"+id.ToString("N").ToUpperInvariant();
        _selected=new(){Id=id,Code=code,Data=new(){Code=code,Building=_building.Text,Room=_room.Text}};
        _grid.SelectedItem=null;Fill();
    }
    private async void Save()
    {
        try
        {
            if(!_api.CanEdit)throw new UnauthorizedAccessException("Hanya Admin / Editor dapat menyimpan.");
            if(_selected is null)return;
            var a=CloudJson.Read<CloudAsset>(CloudJson.Write(_selected));
            a.Data.Name=_name.Text.Trim();a.Data.Category=_category.Text.Trim();a.Data.Building=_building.Text.Trim();a.Data.Room=_room.Text.Trim();
            a.Data.Condition=_condition.Text.Trim();a.Data.Notes=_notes.Text;a.Data.Status=_state.SelectedItem as string??"ACTIVE";
            if(_year.Text.Trim().Length>0&&!int.TryParse(_year.Text,out _))throw new ArgumentException("Tahun harus berupa angka.");
            a.Data.AcquisitionYear=int.TryParse(_year.Text,out var year)?year:null;
            var matches=_store.Assets().Where(x=>x.Id!=a.Id&&x.Data.Status=="ACTIVE"&&string.Equals(x.Name.Trim(),a.Name,StringComparison.OrdinalIgnoreCase)
                &&string.Equals(x.Data.Building.Trim(),a.Data.Building,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Data.Room.Trim(),a.Data.Room,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Code).ToList();
            var allow=false;
            if(a.Version==0&&matches.Count>0){if(!ConfirmDuplicate(matches))return;allow=true;}
            _store.Queue(_api.Session!.Profile.Id,a,allow);_status.Text="Tersimpan di antrean lokal.";Reload();await SyncAsync();
        }
        catch(Exception ex){_status.Text=ex.Message;}
    }
    private bool ConfirmDuplicate(IEnumerable<string> codes)
    {
        // The existing local dialog uses local Session permissions; cloud needs its own verified profile.
        var w=new Window{Owner=this};var p=Forms.Body(w,"Barang serupa ditemukan",620,420);
        Forms.Note(p,"Periksa apakah ini barang fisik yang berbeda.\n"+string.Join("\n",codes.Take(20)));
        var buttons=new DockPanel{Margin=new Thickness(0,20,0,0)};p.Children.Add(buttons);
        var proceed=Button("Tetap Tambah",()=>{if(!_api.CanEdit)throw new UnauthorizedAccessException();w.DialogResult=true;});DockPanel.SetDock(proceed,Dock.Left);buttons.Children.Add(proceed);
        var back=Button("Periksa Kembali",()=>w.DialogResult=false);back.IsDefault=true;back.IsCancel=true;back.HorizontalAlignment=HorizontalAlignment.Right;buttons.Children.Add(back);
        w.Loaded+=(_,_)=>back.Focus();return w.ShowDialog()==true;
    }
    private async Task SyncAsync()
    {
        if(_closed||!_opened)return;
        try{_status.Text=await _sync.RunAsync();if(!_closed&&_opened){Reload();_editor.IsEnabled=_api.CanEdit;}}
        catch(Exception ex){_status.Text=ex.Message;}
    }
    private void Reload()=>_grid.ItemsSource=_store.Assets().Where(a=>(a.Code+" "+a.Name+" "+a.Location).Contains(_search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
    private void ShowQueue()
    {
        var w=new Window{Owner=this};var p=Forms.Body(w,"Antrean dan konflik",800,650);
        foreach(var op in _store.Pending())
        {
            Forms.Note(p,CloudJson.Read<CloudData>(op.Payload).Name+" · "+op.State);
            var local=JsonSerializer.Serialize(JsonDocument.Parse(op.Payload).RootElement,new JsonSerializerOptions{WriteIndented=true});
            var text=new TextBox{Text="LOKAL\n"+local+"\n\nSERVER\n"+(op.Response??"Belum menerima jawaban."),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};p.Children.Add(text);
            if(op.State!="PENDING"&&op.Actor==_api.Session!.Profile.Id.ToString())
            {
                Forms.Button(p,"Gunakan data server / simpan salinan lokal dalam riwayat pemulihan",()=>{_store.Resolve(op,_api.Session.Profile.Id,false);w.Close();Reload();});
                if(op.State is "CONFLICT" or "DUPLICATE")Forms.Button(p,op.State=="DUPLICATE"?"Periksa penambahan barang fisik":"Ajukan ulang perubahan lokal",()=>
                {
                    if(!_api.CanEdit)throw new UnauthorizedAccessException();
                    if(op.State=="DUPLICATE"&&!ConfirmDuplicate(new[]{"Server menemukan barang serupa."}))return;
                    if(op.State=="CONFLICT"&&MessageBox.Show(w,"Versi lokal akan diajukan terhadap versi server yang terlihat di atas. Lanjutkan?","Selesaikan konflik",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                    _store.Resolve(op,_api.Session.Profile.Id,true,op.State=="DUPLICATE");w.Close();_ = SyncAsync();
                });
            }
        }
        if(_store.Pending().Count==0)Forms.Note(p,"Tidak ada antrean.");w.ShowDialog();
    }
    private async void ShowAccounts()
    {
        try
        {
            if(_api.Session?.Profile.Role!="ADMIN")throw new UnauthorizedAccessException("Hanya Admin mengelola akun.");
            var accounts=await _api.AccountsAsync();var w=new Window{Owner=this};var p=Forms.Body(w,"Akun cloud",650,600);
            Forms.Note(p,"Pengguna mendaftar sendiri sebagai Viewer. Admin memilih Editor atau Admin. Admin terakhir tidak dapat dinonaktifkan.");
            var users=Forms.Field(p,"Akun",new ComboBox{ItemsSource=accounts,DisplayMemberPath="Email"});
            var roles=Forms.Field(p,"Peran",new ComboBox{ItemsSource=new[]{"ADMIN","EDITOR","VIEWER"},SelectedIndex=2});
            var active=new CheckBox{Content="Aktif",IsChecked=true};p.Children.Add(active);
            users.SelectionChanged+=(_,_)=>{if(users.SelectedItem is CloudProfile profile){roles.SelectedItem=profile.Role;active.IsChecked=profile.Active;}};
            var result=Forms.Note(p,"");p.Children.Add(Button("Simpan peran",async()=>
            {
                try{if(users.SelectedItem is not CloudProfile profile)return;
                    await _api.RpcAsync("hana_set_role",new{p_user=profile.Id,p_role=roles.SelectedItem,p_active=active.IsChecked==true});
                    result.Text="Peran tersimpan di server.";await SyncAsync();}
                catch(Exception ex){result.Text=ex.Message;}
            }));w.ShowDialog();
        }
        catch(Exception ex){_status.Text=ex.Message;}
    }
    private void TransferLocal()
    {
        if(_api.Session?.Profile.Role!="ADMIN")throw new UnauthorizedAccessException("Transfer memerlukan Admin cloud.");
        if(Session.CurrentUser?.Role!="ADMIN")throw new InvalidOperationException("Buka Inventaris lokal dan masuk sebagai Admin lokal, lalu buka Cloud melalui tombol Cloud v0.4. Ini melindungi data lokal dari transfer oleh akun yang belum dikenal.");
        using var c=Db.Open();var rows=c.Query<InventoryApp.Models.AssetListItem>("SELECT * FROM v_asset_list").ToList();
        if(rows.Any(r=>string.IsNullOrWhiteSpace(r.BuildingName)))throw new InvalidOperationException("Lengkapi gedung setiap aset lokal dahulu.");
        if(MessageBox.Show(this,$"Transfer {rows.Count} aset beserta kode permanen dan salinan riwayat lokal ke antrean cloud? Ini memerlukan konfirmasi duplikat bila server menemukan barang serupa.","Transfer eksplisit",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        AutomaticBackup.SnapshotBeforeMigration(c);
        var source=c.ExecuteScalar<string>("SELECT setting_value FROM app_settings WHERE setting_key='cloud_source_id'");
        if(source is null){source=Guid.NewGuid().ToString();c.Execute("INSERT INTO app_settings(setting_key,setting_value) VALUES('cloud_source_id',@source)",new{source});}
        var existing=_store.Assets().ToDictionary(a=>a.Code,a=>a.Id,StringComparer.Ordinal);var added=0;
        foreach(var r in rows)
        {
            var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(source+"/"+r.AssetCode)).AsSpan(0,16));
            if(existing.TryGetValue(r.AssetCode,out var existingId))
            {
                if(existingId!=id)throw new InvalidOperationException("Kode "+r.AssetCode+" sudah ada dari sumber berbeda. Transfer dihentikan; periksa kedua aset tanpa mengubah kode permanennya. Antrean sebelumnya tetap tersimpan.");
                continue;
            }
            var history=c.Query("SELECT a.*,u.full_name AS original_actor FROM audit_entries a JOIN users u ON u.id=a.user_id WHERE entity_type='Asset' AND entity_id=@Id ORDER BY a.id",new{r.Id});
            _store.Queue(_api.Session.Profile.Id,new CloudAsset{Id=id,Code=r.AssetCode,Data=new(){Code=r.AssetCode,Name=r.ItemTypeName,Category=r.CategoryName,
                Building=r.BuildingName!,Room=r.RoomName??"",Condition=r.ConditionName??"",Status=r.Status,AcquisitionYear=r.AcquisitionYear,Notes=r.Notes??"",LegacyCode=r.LegacyCode,
                LegacyHistory=JsonSerializer.SerializeToElement(new{source,local_asset_id=r.Id,entries=history})}},false);added++;
        }
        _status.Text=$"{added} aset dimasukkan ke antrean. Klik Sinkron sekarang.";Reload();
    }
    private void Backup()
    {
        if(_api.Session?.Profile.Role!="ADMIN")throw new UnauthorizedAccessException("Hanya Admin membuat cadangan portabel.");
        var dialog=new SaveFileDialog{Filter="Cadangan HANA|*.hanaz",FileName="HANA-cloud-cache-"+DateTime.Now.ToString("yyyyMMdd")+".hanaz"};
        if(dialog.ShowDialog(this)!=true)return;var password=Forms.Password(this,"Kata sandi pemulihan",true);if(password is null)return;
        using var c=Db.Open();BackupBundle.Create(c,dialog.FileName,password);_status.Text="Cadangan cache, antrean, inventaris lokal dan riwayat tersimpan. Ini bukan ekspor penuh server Supabase.";
    }
    private void ExportCsv()
    {
        if(_api.Session?.Profile.Role!="ADMIN")throw new UnauthorizedAccessException("Ekspor memerlukan Admin.");
        var save=new SaveFileDialog{Filter="CSV|*.csv",FileName="Inventori-HANA.csv"};if(save.ShowDialog(this)!=true)return;
        static string Q(string v)=>"\""+((v.Length>0&&"=+-@\t\r".Contains(v[0]))?"'"+v:v).Replace("\"","\"\"")+"\"";
        var lines=new List<string>{"Kode Aset,Nama Barang,Gedung,Ruangan,Kategori,Kondisi,Status,Tahun,Catatan,Sinkronisasi"};
        lines.AddRange(_store.Assets().Select(a=>string.Join(",",new[]{a.Code,a.Name,a.Data.Building,a.Data.Room,a.Data.Category,a.Data.Condition,a.Data.Status,a.Data.AcquisitionYear?.ToString()??"",a.Data.Notes,a.State}.Select(Q))));
        File.WriteAllLines(save.FileName,lines,new UTF8Encoding(true));_status.Text="CSV tersimpan.";
    }
    private static void ContactAdmin()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mailto:ezragustinm@gmail.com?subject=Inventori%20HANA%20-%20Bantuan"){UseShellExecute=true});
    private void ShowText(string title,string text){var w=new Window{Owner=this};var p=Forms.Body(w,title,850,650);p.Children.Add(new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.ShowDialog();}
}

