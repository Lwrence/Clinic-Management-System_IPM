using CruzNeryClinic.ViewModels;
using CruzNeryClinic.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CruzNeryClinic.Views
{
    // HelpView displays:
    // - User Manual
    // - Frequently Asked Questions
    // - Search help topics
    // - Print manual button (WebView2 print preview)
    public partial class HelpView : UserControl
    {
        private bool isManualPrintPreviewReady;
        private bool isManualPrintDialogOpening;

        #region Constructor

        public HelpView()
        {
            InitializeComponent();

            DataContextChanged += HelpView_DataContextChanged;
            Unloaded += HelpView_Unloaded;
            ManualPrintWebView.NavigationCompleted += ManualPrintWebView_NavigationCompleted;
        }

        #endregion

        #region WebView2 Print Preview

        private void ManualPrintWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            isManualPrintPreviewReady = e.IsSuccess;
        }

        private void HelpView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is HelpViewModel oldViewModel)
                oldViewModel.PropertyChanged -= HelpViewModel_PropertyChanged;

            if (e.NewValue is HelpViewModel newViewModel)
                newViewModel.PropertyChanged += HelpViewModel_PropertyChanged;
        }

        private void HelpView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is HelpViewModel viewModel)
                viewModel.PropertyChanged -= HelpViewModel_PropertyChanged;
        }

        private async void HelpViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not HelpViewModel viewModel)
                return;

            if (e.PropertyName != nameof(HelpViewModel.ManualPrintPreviewUri))
                return;

            if (viewModel.ManualPrintPreviewUri == null)
                return;

            try
            {
                isManualPrintPreviewReady = false;
                await WebView2EnvironmentService.EnsureInitializedAsync(ManualPrintWebView);
                ManualPrintWebView.Source = viewModel.ManualPrintPreviewUri;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to load manual preview: {ex.Message}",
                    "User Manual Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void OpenManualPrintDialog_Click(object sender, RoutedEventArgs e)
        {
            if (isManualPrintDialogOpening)
                return;

            try
            {
                isManualPrintDialogOpening = true;
                await WebView2EnvironmentService.EnsureInitializedAsync(ManualPrintWebView);

                CoreWebView2? webView = ManualPrintWebView.CoreWebView2;

                if (webView == null)
                {
                    MessageBox.Show(
                        "Print preview is not ready yet. Please try again.",
                        "Print Preview",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    return;
                }

                if (!isManualPrintPreviewReady)
                {
                    MessageBox.Show(
                        "Print preview is still loading. Please try again in a moment.",
                        "Print Preview",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    return;
                }

                ManualPrintWebView.Focus();
                await Task.Delay(250);
                webView.ShowPrintUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open print preview: {ex.Message}",
                    "Print Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                isManualPrintDialogOpening = false;
            }
        }

        #endregion
    }
}
