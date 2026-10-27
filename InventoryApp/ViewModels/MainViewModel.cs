using System.Collections.ObjectModel;
using System.Windows;
using InventoryApp.Data;
using InventoryApp.Models;
using Condition = InventoryApp.Models.Condition;
using InventoryApp.Mvvm;
using InventoryApp.Services;

namespace InventoryApp.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AssetRepository _assets = new();
    private readonly LookupRepository _lookups = new();
    private readonly AssetService _assetService = new();

    public MainViewModel()
    {
        SearchCommand = new RelayCommand(_ => LoadAssets());
        PreviousPageCommand = new RelayCommand(_ => { _page--; LoadAssets(false); }, _ => _page > 0);
        NextPageCommand = new RelayCommand(_ => { _page++; LoadAssets(false); }, _ => (_page + 1) * PageSize < _total);
        ClearFiltersCommand = new RelayCommand(_ =>
        {
            Keyword = "";
            SelectedBuilding = null;
            SelectedStatus = "Semua";
            LoadAssets();
        });
        SaveCommand = new RelayCommand(_ => SaveSelected(), _ => SelectedAsset is not null && CanEdit);

        LoadLookups();
        LoadAssets();
    }

    // ----- Collections shown in the UI ---------------------------------

    public ObservableCollection<AssetListItem> Assets { get; } = new();
    public ObservableCollection<AuditEntry> History { get; } = new();
    public ObservableCollection<Building> Buildings { get; } = new();
    public ObservableCollection<Room> Rooms { get; } = new();
    public ObservableCollection<Room> EditRooms { get; } = new();
    public ObservableCollection<Building> EditBuildings { get; } = new();
    public ObservableCollection<Condition> EditConditions { get; } = new();
    private Building? _editBuilding;
    public Building? EditBuilding
    {
        get => _editBuilding;
        set
        {
            if (!Set(ref _editBuilding,value)) return;
            EditRoom=null; EditRooms.Clear();
            foreach(var room in Rooms.Where(r=>r.BuildingId==value?.Id)) EditRooms.Add(room);
        }
    }
    public ObservableCollection<Condition> Conditions { get; } = new();
    public string[] StatusFilters { get; } = { "Semua", "ACTIVE", "INACTIVE", "DISPOSED" };
    public string[] StatusChoices { get; } = { "ACTIVE", "INACTIVE", "DISPOSED" };

    public RelayCommand SearchCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    private const int PageSize = 100;
    private int _page;
    private int _total;
    public string PageLabel => $"Halaman {_page + 1} / {Math.Max(1, (_total + PageSize - 1) / PageSize)} • {_total} aset";
    public bool IsAdmin => Session.IsAdmin;
    public bool CanEdit => Session.CanEdit;
    public bool IsViewer => !CanEdit;

    // ----- Filters ------------------------------------------------------

    private string _keyword = "";
    public string Keyword { get => _keyword; set => Set(ref _keyword, value); }

    private Building? _selectedBuilding;
    public Building? SelectedBuilding
    {
        get => _selectedBuilding;
        set { if (Set(ref _selectedBuilding, value)) LoadAssets(); }
    }

    private string _selectedStatus = "Semua";
    public string SelectedStatus
    {
        get => _selectedStatus;
        set { if (Set(ref _selectedStatus, value)) LoadAssets(); }
    }

    // ----- Selection and editable fields --------------------------------

    private AssetListItem? _selectedAsset;
    public AssetListItem? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (!Set(ref _selectedAsset, value)) return;
            LoadEditor();
            LoadHistory();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(EmptyDetailMessage));
        }
    }

    public bool HasSelection => SelectedAsset is not null;

    public string EmptyDetailMessage =>
        Assets.Count == 0
            ? "Belum ada aset yang cocok. Ubah kata kunci, atau tambah aset baru."
            : CanEdit?"Pilih satu baris untuk melihat dan mengubah datanya.":"Pilih satu baris untuk melihat detail dan riwayatnya.";

    private Room? _editRoom;
    public Room? EditRoom { get => _editRoom; set => Set(ref _editRoom, value); }

    private Condition? _editCondition;
    public Condition? EditCondition { get => _editCondition; set => Set(ref _editCondition, value); }

    private string _editStatus = "ACTIVE";
    public string EditStatus { get => _editStatus; set => Set(ref _editStatus, value); }

    private string _editYear = "";
    public string EditYear { get => _editYear; set => Set(ref _editYear, value); }

    private string _editNotes = "";
    public string EditNotes { get => _editNotes; set => Set(ref _editNotes, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }

    public string CurrentUserLabel =>
        Session.CurrentUser is null
            ? ""
            : $"{Session.User.FullName} ({Session.User.RoleLabel})";

    // ----- Loading ------------------------------------------------------

    public void LoadLookups()
    {
        Buildings.Clear();
        foreach (var b in _lookups.GetBuildings()) Buildings.Add(b);

        Rooms.Clear();
        foreach (var r in _lookups.GetRooms()) Rooms.Add(r);

        Conditions.Clear();
        foreach (var c in _lookups.GetConditions()) Conditions.Add(c);
    }

    public void LoadAssets(bool resetPage = true)
    {
        try
        {
            var previousId = SelectedAsset?.Id;
            var status = SelectedStatus == "Semua" ? null : SelectedStatus;

            if (resetPage) _page = 0;
            _total = _assets.Count(Keyword, SelectedBuilding?.Id, status);
            _page = Math.Min(_page, Math.Max(0, (_total - 1) / PageSize));

            Assets.Clear();
            foreach (var a in _assets.Search(Keyword, SelectedBuilding?.Id, status, _page, PageSize))
                Assets.Add(a);

            StatusMessage = $"{Assets.Count} aset ditampilkan.";
            OnPropertyChanged(nameof(EmptyDetailMessage));
            OnPropertyChanged(nameof(PageLabel));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            SelectedAsset = Assets.FirstOrDefault(a => a.Id == previousId);
        }
        catch (Exception ex)
        {
            Report(ex, "Gagal memuat daftar aset.");
        }
    }

    private void LoadEditor()
    {
        if (SelectedAsset is null) return;

        EditBuildings.Clear();foreach(var b in Buildings)EditBuildings.Add(b);
        if(SelectedAsset.BuildingId is int bid && !EditBuildings.Any(b=>b.Id==bid))
            EditBuildings.Add(new Building {Id=bid,Name=SelectedAsset.BuildingName+" (nonaktif)"});
        EditBuilding = EditBuildings.FirstOrDefault(b => b.Id == SelectedAsset.BuildingId);
        if(SelectedAsset.RoomId is int rid && !EditRooms.Any(r=>r.Id==rid))
            EditRooms.Add(new Room {Id=rid,Name=SelectedAsset.RoomName+" (nonaktif)",BuildingName=SelectedAsset.BuildingName??"",BuildingId=SelectedAsset.BuildingId??0});
        EditRoom = EditRooms.FirstOrDefault(r => r.Id == SelectedAsset.RoomId);
        EditConditions.Clear();foreach(var c in Conditions)EditConditions.Add(c);
        if(SelectedAsset.ConditionId is int cid && !EditConditions.Any(c=>c.Id==cid))
            EditConditions.Add(new Condition {Id=cid,Name=SelectedAsset.ConditionName+" (nonaktif)",Code=SelectedAsset.ConditionCode??""});
        EditCondition = EditConditions.FirstOrDefault(c => c.Id == SelectedAsset.ConditionId);
        EditStatus = SelectedAsset.Status;
        EditYear = SelectedAsset.AcquisitionYear?.ToString() ?? "";
        EditNotes = SelectedAsset.Notes ?? "";
    }

    private void LoadHistory()
    {
        History.Clear();
        if (SelectedAsset is null) return;

        try
        {
            foreach (var h in _assets.GetHistory(SelectedAsset.Id)) History.Add(h);
        }
        catch (Exception ex)
        {
            Report(ex, "Gagal memuat riwayat perubahan.");
        }
    }

    // ----- Saving -------------------------------------------------------

    private void SaveSelected()
    {
        if (SelectedAsset is null) return;

        int? year = null;
        if (!string.IsNullOrWhiteSpace(EditYear))
        {
            if (!int.TryParse(EditYear.Trim(), out var parsed) || parsed < 1900 || parsed > 2100)
            {
                MessageBox.Show("Tahun perolehan harus berupa angka empat digit, misalnya 2019.",
                    "Periksa isian", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            year = parsed;
        }

        var changes = new AssetChangeSet
        {
            ExpectedVersion = SelectedAsset.RowVersion,
            BuildingId = EditBuilding?.Id,
            RoomId = EditRoom?.Id,
            ConditionId = EditCondition?.Id,
            Status = EditStatus,
            AcquisitionYear = year,
            Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim()
        };

        try
        {
            var changed = _assetService.UpdateAsset(SelectedAsset.Id, changes, Session.User.Id);
            var message = changed == 0
                ? "Tidak ada perubahan untuk disimpan."
                : $"Perubahan tersimpan ({changed} kolom) pada {SelectedAsset.AssetCode}.";

            LoadAssets(false);
            LoadHistory();
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            Report(ex, "Perubahan gagal disimpan.");
        }
    }

    public void CreateAssets(int itemTypeId, int quantity, int? roomId, int? conditionId,
        int? acquisitionYear, string? notes, int? buildingId=null,bool duplicateOverride=false)
    {
        var codes = _assetService.CreateAssets(
            itemTypeId, quantity, roomId, conditionId, acquisitionYear, notes, Session.User.Id,buildingId,duplicateOverride);

        var message = codes.Count == 1
            ? $"Aset {codes[0]} dibuat."
            : $"{codes.Count} aset dibuat: {codes[0]} sampai {codes[^1]}.";

        LoadAssets();
        StatusMessage = message;
    }

    private static void Report(Exception ex, string what)
        => MessageBox.Show($"{what}\n\n{ex.Message}", "Terjadi kesalahan",
            MessageBoxButton.OK, MessageBoxImage.Error);
}
