using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InventoryApp.Views;

internal static class Forms
{
    public static StackPanel Body(Window window,string title,double width=540,double height=620)
    {
        window.Title=title; window.Width=Math.Min(width,SystemParameters.WorkArea.Width-40); window.Height=Math.Min(height,SystemParameters.WorkArea.Height-40);
        window.MinWidth=420; window.MinHeight=380;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/InventoryApp;component/Assets/App.ico"));
        var panel=new StackPanel {Margin=new Thickness(24)};
        window.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock {Text=title,FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        return panel;
    }
    public static TextBlock Note(Panel p,string text)
    {
        var note=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        p.Children.Add(note); return note;
    }
    public static T Field<T>(Panel p,string label,T control) where T:FrameworkElement
    {
        Note(p,label); p.Children.Add(control); return control;
    }
    public static Button Button(Panel p,string text,Action action)
    {
        var button=new Button {Content=text,Margin=new Thickness(0,8,8,0),Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Left};
        button.Click+=(_,_)=>{try {action();} catch(Exception ex) {MessageBox.Show(ex.Message,"Periksa kembali",MessageBoxButton.OK,MessageBoxImage.Warning);}};
        p.Children.Add(button);return button;
    }
    public static string? Ask(Window owner,string title,string initial="")
    {
        var w=new Window {Owner=owner}; var p=Body(w,title,460,260);var input=new TextBox {Text=initial};p.Children.Add(input);
        Button(p,"Simpan",()=>{if(string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("Isian wajib diisi.");w.DialogResult=true;});
        input.Focus();return w.ShowDialog()==true?input.Text.Trim():null;
    }
    public static string? Password(Window owner,string title,bool confirm)
    {
        var w=new Window {Owner=owner}; var p=Body(w,title,500,380);
        var first=Field(p,"Kata sandi",new PasswordBox());
        PasswordBox? second=confirm?Field(p,"Ulangi kata sandi",new PasswordBox()):null;
        Note(p,confirm?"Minimal 12 karakter. Simpan kata sandi cadangan di tempat aman; diperlukan saat memulihkan di komputer lain.":"Masukkan kata sandi yang dipakai saat membuat cadangan.");
        Button(p,"Lanjut",()=>{if(confirm && (first.Password.Length<12 || first.Password!=second!.Password)) throw new ArgumentException("Minimal 12 karakter dan kedua isian harus sama.");w.DialogResult=true;});
        return w.ShowDialog()==true?first.Password:null;
    }
}
