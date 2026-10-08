using CruzNeryClinic.ViewModels;
using CruzNeryClinic.Views;
using CruzNeryClinic.Models;
using System.Windows;
using System.Windows.Controls;

namespace CruzNeryClinic
{
    // MainWindow is the main container of the application.
    // It is responsible for switching between major screens such as Splash and Login.
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Show the splash screen first when the application opens.
            ShowSplashScreen();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (MainContent.Content is MainShellView shell && shell.DataContext is MainShellViewModel viewModel)
                viewModel.ResetMigrationSession();
            CruzNeryClinic.Services.SessionService.Logout();
            base.OnClosed(e);
        }

        private async void ShowSplashScreen()
        {
            // Create the splash ViewModel.
            SplashViewModel splashViewModel = new SplashViewModel();

            // Create the splash View.
            SplashView splashView = new SplashView();

            // Connect the ViewModel to the View.
            splashView.DataContext = splashViewModel;

            // Display the splash screen inside MainContent.
            MainContent.Content = splashView;

            // When the splash finishes, move to the login screen.
            splashViewModel.SplashFinished += ShowLoginScreen;

            // Start the splash delay.
            await splashViewModel.StartSplashAsync();
        }

        private void ShowLoginScreen()
        {
            // Create the login ViewModel.
            LoginViewModel loginViewModel = new LoginViewModel();

            // Create the login View.
            LoginView loginView = new LoginView();

            // Connect the ViewModel to the View.
            loginView.DataContext = loginViewModel;

            // When login succeeds, move to dashboard later.
            loginViewModel.LoginSucceeded += ShowDashboardScreen;

            // When forgot password is clicked, we will open ForgotPasswordView later.
            loginViewModel.ForgotPasswordRequested += ShowForgotPasswordScreen;

            // Display the login screen.
            MainContent.Content = loginView;
        }
        private void ShowDashboardScreen()
        {
            MainShellViewModel mainShellViewModel = new MainShellViewModel();
            MainShellView mainShellView = new MainShellView();

            mainShellView.DataContext = mainShellViewModel;

            mainShellViewModel.LogoutRequested += ShowLoginScreen;

            MainContent.Content = mainShellView;
        }

        private void ShowForgotPasswordScreen()
        {
            // Create the Forgot Password ViewModel.
            ForgotPasswordViewModel forgotPasswordViewModel = new ForgotPasswordViewModel();

            // Create the Forgot Password View.
            ForgotPasswordView forgotPasswordView = new ForgotPasswordView();

            // Connect the ViewModel to the View.
            forgotPasswordView.DataContext = forgotPasswordViewModel;

            // If the user ID is found, proceed to Security Questions screen.
            forgotPasswordViewModel.UserFound += ShowSecurityQuestionsScreen;

            // If back is clicked, return to Login screen.
            forgotPasswordViewModel.BackToLoginRequested += ShowLoginScreen;

            // Display the Forgot Password screen.
            MainContent.Content = forgotPasswordView;
        }
        private void ShowSecurityQuestionsScreen(User user)
        {
            // Create Security Questions ViewModel and View.
            SecurityQuestionsViewModel securityQuestionsViewModel = new SecurityQuestionsViewModel(user);
            SecurityQuestionsView securityQuestionsView = new SecurityQuestionsView();

            // Connect the ViewModel to the View.
            securityQuestionsView.DataContext = securityQuestionsViewModel;

            // If answers are correct, proceed to Create New Password later.
            securityQuestionsViewModel.SecurityPassed += ShowCreateNewPasswordScreen;

            // If Back is clicked, return to Forgot Password screen.
            securityQuestionsViewModel.BackToForgotPasswordRequested += ShowForgotPasswordScreen;

            // Display Security Questions screen.
            MainContent.Content = securityQuestionsView;
        }
        private void ShowCreateNewPasswordScreen(User user)
        {
            // Create the reset password ViewModel and View.
            CreateNewPasswordViewModel createNewPasswordViewModel = new CreateNewPasswordViewModel(user);
            CreateNewPasswordView createNewPasswordView = new CreateNewPasswordView();

            // Connect the ViewModel to the View.
            createNewPasswordView.DataContext = createNewPasswordViewModel;

            // After password reset succeeds, return to Login screen.
            createNewPasswordViewModel.PasswordResetSucceeded += ShowLoginScreen;

            // Back returns to Security Questions.
            createNewPasswordViewModel.BackToSecurityQuestionsRequested += ShowSecurityQuestionsScreen;

            // Display the Create New Password screen.
            MainContent.Content = createNewPasswordView;
        }
    }
}