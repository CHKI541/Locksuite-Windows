using System.Windows;
using System.Windows.Input;

namespace LockSuite.UI.Views;

public partial class PasswordDialog : Window
{
    public string Password { get; private set; } = "";

    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtPassword.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Password = TxtPassword.Password;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnOk_Click(sender, e);
        }
    }
}
