using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LockSuite.Core;
using LockSuite.UI.ViewModels;
using LockSuite.UI.Views;

namespace LockSuite.UI;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _trialHandling;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ViewModel.TrialAwaitingDecision += OnTrialAwaiting;
        };
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && btn.Tag is string tag)
        {
            ViewEstado.Visibility = tag == "Estado" ? Visibility.Visible : Visibility.Collapsed;
            ViewListaBlanca.Visibility = tag == "ListaBlanca" ? Visibility.Visible : Visibility.Collapsed;
            ViewRecientes.Visibility = tag == "Recientes" ? Visibility.Visible : Visibility.Collapsed;
            ViewConfig.Visibility = tag == "Configuracion" ? Visibility.Visible : Visibility.Collapsed;
            ViewAyuda.Visibility = tag == "Ayuda" ? Visibility.Visible : Visibility.Collapsed;
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

            // Desactivar un paquete de bloqueo (p. ej. el del buscador de Google y Gemini) pide confirmación.
            if (pack.Blocks && chk.IsChecked == false)
            {
                var confirm = MessageBox.Show(this,
                    $"Vas a desactivar «{pack.Name}».\n\nMientras esté desactivado, esos sitios dejan de estar bloqueados si algún paquete los permite (por ejemplo, el buscador de Google o Gemini).\n\n¿Continuar?",
                    "Desactivar bloqueo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    chk.IsChecked = true;
                    pack.Enabled = true;
                    return;
                }
            }

            bool ok = await ViewModel.TogglePackAsync(pack);
            if (!ok)
            {
                chk.IsChecked = !chk.IsChecked;
                MessageBox.Show(this, "Error al actualizar el estado del paquete", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ----------------- Servicios con un clic ("Permitir Gmail", etc.) -----------------

    private async void BtnServiceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not PackViewModel pack) return;
        if (!await EnsureAuthenticatedAsync()) return;

        bool previous = pack.Enabled;
        pack.Enabled = !previous;   // TogglePackAsync envía el estado nuevo

        bool ok = await ViewModel.TogglePackAsync(pack);
        if (!ok)
        {
            pack.Enabled = previous;
            MessageBox.Show(this, $"No se pudo actualizar «{pack.Title}». Verificá que el servicio LockSuite esté en ejecución.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (pack.Enabled)
        {
            MessageBox.Show(this,
                $"{pack.Title} quedó permitido.\n\nSi el navegador ya tenía la página abierta, recargala (F5). " +
                "El buscador de Google y Gemini siguen bloqueados.",
                "Servicio permitido", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this,
                $"{pack.Title} quedó bloqueado.\n\nLo que ya estaba abierto puede seguir funcionando unos minutos " +
                "(hasta que venzan la caché de DNS y la autorización temporal).",
                "Servicio bloqueado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnServiceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string url) return;

        // Solo direcciones https del catálogo de servicios.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo abrir el navegador: {ex.Message}\n\nDirección: {uri.AbsoluteUri}",
                "Abrir servicio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnAddDomain_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        // Solo paquetes del tutor: los de fábrica se manejan con sus botones y los de bloqueo no reciben permisos.
        var existingPacks = ViewModel.CustomPackNames();
        var dlg = new AddDomainDialog(existingPacks) { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            // Aviso si el patrón abarca el buscador de Google o Gemini (p. ej. "google.com").
            if (Presets.CoversKosherBlockedSites(dlg.Domain))
            {
                var confirm = MessageBox.Show(this,
                    $"«{dlg.Domain}» abarca el buscador de Google y/o Gemini.\n\n" +
                    "Esas partes van a seguir bloqueadas por el «Bloqueo fijo» mientras esté activo, pero se habilitarían muchos otros servicios de Google (Play, Noticias, Mapas, etc.).\n\n" +
                    "Para Gmail, Chat, Drive y Fotos conviene usar sus botones en «Lista Blanca». ¿Agregar igual?",
                    "Dominio muy amplio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

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

            bool inBlockPack = ViewModel.Packs.Any(p => p.Blocks && p.Domains.Contains(domain));
            string question = inBlockPack
                ? $"«{domain}» está en un paquete de bloqueo. Si lo quitás, deja de estar bloqueado cuando algún paquete lo permita.\n\n¿Quitarlo del bloqueo?"
                : $"¿Seguro que desea eliminar '{domain}' de la lista blanca?\n\n(Se quita de todos los paquetes donde aparece. Se puede volver a agregar con «+ Agregar Dominio».)";
            var result = MessageBox.Show(this, question, "Confirmar Eliminación", MessageBoxButton.YesNo,
                inBlockPack ? MessageBoxImage.Warning : MessageBoxImage.Question);
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
            // Lo que está en un bloqueo fijo (buscador de Google, Gemini) no se habilita agregándolo a la lista.
            if (ViewModel.IsHardBlocked(domain))
            {
                MessageBox.Show(this,
                    $"«{domain}» está bloqueado a propósito por un paquete de bloqueo (por ejemplo, «Bloqueo fijo: buscador de Google y Gemini»).\n\n" +
                    "Agregarlo a la lista blanca no lo habilitaría. Si de verdad hace falta, primero hay que desactivar ese bloqueo en «Lista Blanca».",
                    "Bloqueo fijo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!await EnsureAuthenticatedAsync()) return;

            bool ok = await ViewModel.AddDomainAsync(domain, Presets.CustomPackName);
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

    private async void ChkLearningMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk)
        {
            if (!await EnsureAuthenticatedAsync())
            {
                chk.IsChecked = !chk.IsChecked; // Revertir si no se autenticó
                return;
            }

            bool desired = chk.IsChecked == true;
            bool ok = await ViewModel.SetLearningModeAsync(desired);
            if (!ok)
            {
                chk.IsChecked = !chk.IsChecked;
                MessageBox.Show(this, "No se pudo cambiar el modo aprendizaje.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ChkAllowLocalNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk)
        {
            if (!await EnsureAuthenticatedAsync())
            {
                chk.IsChecked = !chk.IsChecked;
                return;
            }
            bool desired = chk.IsChecked == true;
            bool ok = await ViewModel.SetAllowLocalNetworkAsync(desired);
            if (!ok)
            {
                chk.IsChecked = !chk.IsChecked;
                MessageBox.Show(this, "No se pudo cambiar el permiso de red local.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ----------------- Wi-Fi de hotel / portal cautivo -----------------

    private async void BtnCaptivePortal_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;

        var confirm = MessageBox.Show(this,
            "Se abrirá una ventana de 5 minutos para que puedas entrar a la página de la red Wi-Fi (la de aceptar términos o poner la clave).\n\n" +
            "Durante esa ventana se abre solo la red local, no internet: los sitios bloqueados siguen bloqueados. Al terminar, el filtro vuelve solo.\n\n¿Continuar?",
            "Modo portal cautivo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var (ok, err) = await ViewModel.StartCaptivePortalAsync(5);
        if (ok)
        {
            MessageBox.Show(this,
                "Listo. Abrí el navegador: debería aparecer la página de la red Wi-Fi. Si no aparece sola, entrá a cualquier dirección y la red te va a redirigir.",
                "Modo portal cautivo activo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, err ?? "No se pudo abrir el modo portal cautivo.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnCaptivePortalEnd_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureAuthenticatedAsync()) return;
        await ViewModel.EndCaptivePortalAsync();
    }

    // ----------------- Modo de prueba (trial) -----------------

    private void OnTrialAwaiting()
    {
        // La prueba venció: mostrar el diálogo (en el hilo de UI).
        _ = Dispatcher.InvokeAsync(ShowTrialDialogAsync);
    }

    private async Task ShowTrialDialogAsync()
    {
        if (_trialHandling) return;
        _trialHandling = true;
        try
        {
            var dlg = new TrialEndedDialog(ViewModel.TrialRequireTutorToRemove) { Owner = this };
            bool? r = dlg.ShowDialog();
            if (r == true)
            {
                await DoTrialKeepAsync();
            }
            else if (dlg.RemoveRequested)
            {
                await DoTrialRemoveAsync();
            }
            // Si cerró el diálogo sin decidir, el banner en "Estado" queda disponible para decidir luego.
        }
        finally
        {
            _trialHandling = false;
        }
    }

    private async void BtnTrialKeep_Click(object sender, RoutedEventArgs e) => await DoTrialKeepAsync();

    private async void BtnTrialRemove_Click(object sender, RoutedEventArgs e) => await DoTrialRemoveAsync();

    private async Task DoTrialKeepAsync()
    {
        var (ok, err) = await ViewModel.DecideTrialAsync(true, null);
        if (ok)
        {
            MessageBox.Show(this, "LockSuite quedará instalado de forma permanente en este equipo. A partir de ahora, solo el administrador podrá quitarlo.", "LockSuite conservado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, err ?? "No se pudo registrar la decisión.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DoTrialRemoveAsync()
    {
        var confirm = MessageBox.Show(this,
            "Se desinstalará LockSuite por completo y se restaurará el acceso a internet sin filtro. ¿Continuar?",
            "Quitar LockSuite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        string? token = null;
        if (ViewModel.TrialRequireTutorToRemove)
        {
            // La prueba se configuró para exigir la contraseña del tutor al quitar.
            if (!await EnsureAuthenticatedAsync()) return;
            token = ViewModel.SessionToken;
        }

        var (ok, err) = await ViewModel.DecideTrialAsync(false, token);
        if (ok)
        {
            MessageBox.Show(this, "LockSuite se está quitando de este equipo. La aplicación se cerrará.", "Quitando LockSuite", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        else
        {
            MessageBox.Show(this, err ?? "No se pudo quitar LockSuite.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
