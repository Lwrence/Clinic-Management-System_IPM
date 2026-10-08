using System.Windows;
using System.Windows.Controls;
using CruzNeryClinic.ViewModels;

namespace CruzNeryClinic.Views;

public partial class EmailNotificationsView : UserControl
{
    public EmailNotificationsView() => InitializeComponent();
    private void GmailPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmailNotificationsViewModel viewModel)
            viewModel.SetAppPassword(((PasswordBox)sender).Password);
    }
    private void GmailPasswordVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) ((PasswordBox)sender).Clear();
    }
}
