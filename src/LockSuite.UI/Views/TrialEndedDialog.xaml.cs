using System.Windows;

namespace LockSuite.UI.Views;

public partial class TrialEndedDialog : Window
{
    /// <summary>True si el usuario eligió "No, quitar".</summary>
    public bool RemoveRequested { get; private set; }

    public TrialEndedDialog(bool requireTutorToRemove)
    {
        InitializeComponent();
        if (requireTutorToRemove)
        {
            TutorNote.Visibility = Visibility.Visible;
        }
    }

    private void BtnKeep_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested = false;
        DialogResult = true;   // conservar
        Close();
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = false;  // quitar
        Close();
    }
}
