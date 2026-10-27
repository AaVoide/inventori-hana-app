using System.Windows;
using System.Windows.Input;
using InventoryApp.Data;
using InventoryApp.Services;

namespace InventoryApp.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth = new();

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TrySignIn();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e) => TrySignIn();

    private void TrySignIn()
    {
        HideError();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (username.Length == 0 || password.Length == 0)
        {
            ShowError("Isi nama pengguna dan kata sandi.");
            return;
        }

        LoginButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            Db.TestConnection();

            var user = _auth.Login(username, password);
            if (user is null)
            {
                ShowError("Nama pengguna atau kata sandi salah.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            Session.CurrentUser = user;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError("Database tidak dapat dibuka. Pastikan folder aplikasi dapat ditulis.\n\n"
                      + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
