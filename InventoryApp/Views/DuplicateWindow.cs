using System.Windows;
using System.Windows.Controls;
using InventoryApp.Services;

namespace InventoryApp.Views;

public sealed class DuplicateWindow:Window
{
    public DuplicateWindow(IEnumerable<string> codes)
    {
        var p=Forms.Body(this,"Periksa kemungkinan duplikat",600,390);
        Forms.Note(p,"Barang dengan nama dan lokasi yang sama sudah tercatat. Pastikan ini barang fisik yang berbeda. Keputusan Tetap Tambah akan dicatat dalam riwayat.");
        Forms.Note(p,string.Join(", ",codes));
        var actions=new Grid {Margin=new Thickness(0,24,0,0)};p.Children.Add(actions);
        var add=new Button {Content="Tetap Tambah",HorizontalAlignment=HorizontalAlignment.Left};
        var check=new Button {Content="Periksa Kembali",IsDefault=true,IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Style=(Style)FindResource("PrimaryButton")};
        add.Click+=(_,_)=>{AccessControl.RequireEditor(Session.User.Id);DialogResult=true;};
        check.Click+=(_,_)=>DialogResult=false;
        actions.Children.Add(add);actions.Children.Add(check);Loaded+=(_,_)=>check.Focus();
    }
}
