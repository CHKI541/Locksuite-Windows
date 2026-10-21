using System.Collections.Generic;
using System.Windows;

namespace LockSuite.UI.Views;

public partial class AddDomainDialog : Window
{
    public string Domain { get; private set; } = "";
    public string? SelectedPack { get; private set; }

    public AddDomainDialog(IEnumerable<string> existingPacks, string? initialDomain = null)
    {
        InitializeComponent();

        foreach (var pack in existingPacks)
        {
            CmbPacks.Items.Add(pack);
        }

        if (CmbPacks.Items.Count > 0)
        {
            CmbPacks.SelectedIndex = 0;
        }

        if (!string.IsNullOrEmpty(initialDomain))
        {
            TxtDomain.Text = initialDomain;
        }

        Loaded += (_, _) => TxtDomain.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Domain = TxtDomain.Text.Trim();
        SelectedPack = CmbPacks.Text?.Trim();

        if (string.IsNullOrWhiteSpace(Domain))
        {
            MessageBox.Show(this, "Por favor ingrese un nombre de dominio.", "Dato requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
