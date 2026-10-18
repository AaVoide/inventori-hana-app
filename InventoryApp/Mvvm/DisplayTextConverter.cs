using System.Globalization;
using System.Windows.Data;

namespace InventoryApp.Mvvm;

public class DisplayTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            "ACTIVE" => "Aktif", "INACTIVE" => "Tidak aktif", "DISPOSED" => "Dilepas",
            "CREATE" => "Dibuat", "UPDATE" => "Diubah", "MOVE" => "Dipindahkan", "STATUS_CHANGE" => "Status",
            "room_id" => "Ruangan", "condition_id" => "Kondisi", "status" => "Status",
            "acquisition_year" => "Tahun", "notes" => "Keterangan", _ => value ?? ""
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
