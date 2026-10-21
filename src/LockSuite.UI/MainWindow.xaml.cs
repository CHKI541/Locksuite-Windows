using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LockSuite.UI.ViewModels;
using LockSuite.UI.Views;

namespace LockSuite.UI;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && btn.Tag is string tag)
        {
            ViewEstado.Visibility = tag == "Estado" ? Visibility.Visible : Visibility.Collapsed;
            ViewListaBlanca.Visibility = tag == "ListaBlanca" ? Visibility.Visible : Visibility.Collapsed;
            ViewRecientes.Visibility = tag == "Recientes" ? Visibility.Visible : Visibility.Collapsed;
            ViewConfig.Visibility = tag == "Configuracion" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshDataAsync();
    }

    private async void BtnAuthTutor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsAuthenticated)
        {
            ViewModel.LockTutor();
            MessageBox.Show(this, "Sesión de tutor bloqueada. Los cambios quedan restringidos.", "Sesión Cerrada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await EnsureAuthenticatedAsync();
    }

    private async System.Threading.Tasks.Task<bool> EnsureAuthenticatedAsync()
    {
        if (ViewModel.IsAuthenticated) return true;

        if (!ViewModel.IsPasswordConfigured)
        {
            var initDlg = new PasswordDialog
            {
                Owner = this,
                Title = "Configurar Contraseña Maestra Inicial"
            };
            if (initDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(initDlg.Password))
            {
                var (initSuccess, initErr) = await ViewModel.SetInitialPasswordAsync(initDlg.Password);
                if (initSuccess)
                {
                    await ViewModel.AuthenticateTutorAsync(initDlg.Password);
                    MessageBox.Show(this, "Contraseña maestra configurada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }
                else
                {
                    MessageBox.Show(this, initErr ?? "No se pudo configurar la contraseña", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            return false;
        }

        var dlg = new PasswordDialog { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var (success, error) = await ViewModel.AuthenticateTutorAsync(dlg.Password);
            if (success)
            {
                return true;
            }
            else
            {
                MessageBox.Show(this, error ?? "Contraseña incorrecta", "Autenticación Denegada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        return false;
    }

    private async void ChkPackToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk && chk.DataContext is PackViewModel pack)
        {
            if (!await EnsureAuthenticatedAsync())
            {
                chk.IsChecked = !chk.IsChecked; // Revertir check
                return;
            }

            bool ok = await ViewModel.TogglePackAsync(pack);
            if (!ok)
            {
                chk.IsChecked = !chk.IsChecked;
                MessageBox.Show(this, "Error al actualizar el estado del paquete", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnAddDomain_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        var existingPacks = ViewModel.Packs.Select(p => p.Name).ToList();
        var dlg = new AddDomainDialog(existingPacks) { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            bool ok = await ViewModel.AddDomainAsync(dlg.Domain, dlg.SelectedPack);
            if (ok)
            {
                MessageBox.Show(this, $"Dominio '{dlg.Domain}' agregado a la lista blanca.", "Dominio Agregado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "No se pudo agregar el dominio.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnRemoveDomain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            if (!await EnsureAuthenticatedAsync()) return;

            var result = MessageBox.Show(this, $"¿Seguro que desea eliminar '{domain}' de la lista blanca?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                bool ok = await ViewModel.RemoveDomainAsync(domain);
                if (!ok)
                {
                    MessageBox.Show(this, "No se pudo eliminar el dominio.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void BtnAllowBlocked_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            if (!await EnsureAuthenticatedAsync()) return;

            bool ok = await ViewModel.AddDomainAsync(domain, "Personalizados");
            if (ok)
            {
                MessageBox.Show(this, $"El dominio '{domain}' ha sido permitido y agregado a la lista blanca.", "Dominio Permitido", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "No se pudo autorizar el dominio.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnClearBlocked_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        bool ok = await ViewModel.ClearRecentBlockedAsync();
        if (ok)
        {
            MessageBox.Show(this, "Historial de dominios bloqueados vaciado.", "Historial Limpio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        var dlgOld = new PasswordDialog
        {
            Owner = this,
            Title = "Confirmar Contraseña Actual"
        };
        if (dlgOld.ShowDialog() != true) return;

        var dlgNew = new PasswordDialog
        {
            Owner = this,
            Title = "Nueva Contraseña Maestra"
        };
        if (dlgNew.ShowDialog() != true) return;

        var (success, error) = await ViewModel.ChangePasswordAsync(dlgOld.Password, dlgNew.Password);
        if (success)
        {
            MessageBox.Show(this, "Contraseña maestra actualizada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, error ?? "Error al cambiar contraseña", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnTestWfp_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        string result = await ViewModel.RunWfpTestAsync();
        MessageBox.Show(this, result, "Prueba WFP", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
