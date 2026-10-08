using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using CruzNeryClinic.Views;
using System;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CruzNeryClinic.ViewModels
{
    // MainShellViewModel controls the logged-in shell layout.
    // It keeps the sidebar visible while changing only the right-side module content.
    public class MainShellViewModel : BaseViewModel
    {
        private const string EmployeePrivacyAcknowledgementVersion = "CNDC-EMP-DPA-2026-01";
        private const string EmployeePrivacyNoticeBody =
            "CRUZ-NERY DENTAL CLINIC EMPLOYEE PRIVACY NOTICE AND ACKNOWLEDGEMENT\n\n" +
            "This system is used only by authorized Cruz-Nery Dental Clinic staff. Before accessing the dashboard and clinic modules, each staff user must acknowledge how their data is handled while using the system.\n\n" +
            "The clinic collects and stores staff account information such as name, contact number, username, role, access permissions, security questions, login activity, and system activity records. Passwords and security answers are stored as protected hashes, not as plain text.\n\n" +
            "This information is used to authenticate users, apply role-based access, protect patient and clinic records, maintain audit trails, investigate system activity when needed, and support clinic operations, security, and accountability.\n\n" +
            "Staff activity inside the system may be recorded, including login events and actions involving users, patients, appointments, billing, inventory, reports, maintenance, backups, and other protected clinic records.\n\n" +
            "Only authorized personnel may access staff information and activity records according to their role and clinic responsibilities. Staff must use their own account, keep credentials confidential, and access patient or clinic data only for legitimate work purposes.\n\n" +
            "By acknowledging this notice, the staff member confirms that they understand the clinic may process their account and activity data for system security, audit trail, compliance, and clinic accountability.";

        private readonly UserRepository userRepository;
        private UserControl _currentModuleView;
        private string _selectedModule = "Dashboard";
        private bool isEmployeePrivacyAcknowledgementOpen;
        private bool hasConfirmedEmployeePrivacyNotice;
        private bool isLoginSuccessToastVisible;
        private readonly DispatcherTimer loginSuccessToastTimer;

        public event Action? LogoutRequested;

        public MainShellViewModel()
        {
            userRepository = new UserRepository();

            // Default module after login.
            _currentModuleView = CreateDashboardView();

            IsEmployeePrivacyAcknowledgementOpen = NeedsEmployeePrivacyAcknowledgement();

            AcknowledgeEmployeePrivacyCommand = new RelayCommand(
                AcknowledgeEmployeePrivacy,
                () => HasConfirmedEmployeePrivacyNotice
            );

            loginSuccessToastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            loginSuccessToastTimer.Tick += (_, _) =>
            {
                loginSuccessToastTimer.Stop();
                IsLoginSuccessToastVisible = false;
            };

            ShowLoginSuccessToast();
        }

        public UserControl CurrentModuleView
        {
            get => _currentModuleView;
            set => SetProperty(ref _currentModuleView, value);
        }

        public string SelectedModule
        {
            get => _selectedModule;
            set => SetProperty(ref _selectedModule, value);
        }

        public bool IsEmployeePrivacyAcknowledgementOpen
        {
            get => isEmployeePrivacyAcknowledgementOpen;
            set => SetProperty(ref isEmployeePrivacyAcknowledgementOpen, value);
        }

        public bool HasConfirmedEmployeePrivacyNotice
        {
            get => hasConfirmedEmployeePrivacyNotice;
            set
            {
                if (SetProperty(ref hasConfirmedEmployeePrivacyNotice, value))
                    (AcknowledgeEmployeePrivacyCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        public string EmployeePrivacyNoticeText => EmployeePrivacyNoticeBody;

        public ICommand AcknowledgeEmployeePrivacyCommand { get; }

        public bool IsLoginSuccessToastVisible
        {
            get => isLoginSuccessToastVisible;
            set => SetProperty(ref isLoginSuccessToastVisible, value);
        }

        public void NavigateTo(string moduleName)
        {
            if (IsEmployeePrivacyAcknowledgementOpen)
                return;

            if (!SessionService.CanAccessModule(moduleName))
                return;

            SelectedModule = moduleName;

            CurrentModuleView = moduleName switch
            {
                "Dashboard" => CreateDashboardView(),
                "ManageUsers" => CreateUserManagementView(),
                "Patients" => CreatePatientManagementView(),
                "DataMigration" => new DataMigrationView { DataContext = new DataMigrationViewModel() },
                "Appointment" => CreateAppointmentManagementView(),
                "Billing" => CreateBillingView(),
                "Inventory" => CreateInventoryView(),
                "Maintenance" => CreateMaintenanceView(),
                "Reports" => CreateReportsView(),
                "Help" => CreateHelpView(),
                _ => CreateDashboardView()
            };
        }

        public void Logout()
        {
            ResetMigrationSession();
            SessionService.Logout();
            LogoutRequested?.Invoke();
        }

        public void ResetMigrationSession()
        {
            loginSuccessToastTimer.Stop();
            if (CurrentModuleView.DataContext is DataMigrationViewModel migration)
                migration.ResetSession();
        }

        private bool NeedsEmployeePrivacyAcknowledgement()
        {
            return SessionService.CurrentUser == null
                || !SessionService.CurrentUser.HasEmployeePrivacyAcknowledgement
                || SessionService.CurrentUser.EmployeePrivacyAcknowledgementVersion != EmployeePrivacyAcknowledgementVersion;
        }

        private void AcknowledgeEmployeePrivacy()
        {
            if (SessionService.CurrentUser == null || !HasConfirmedEmployeePrivacyNotice)
                return;

            userRepository.RecordEmployeePrivacyAcknowledgement(
                SessionService.CurrentUser,
                EmployeePrivacyAcknowledgementVersion
            );

            IsEmployeePrivacyAcknowledgementOpen = false;
        }

        private void ShowLoginSuccessToast()
        {
            IsLoginSuccessToastVisible = true;
            loginSuccessToastTimer.Stop();
            loginSuccessToastTimer.Start();
        }

        private DashboardView CreateDashboardView()
        {
            DashboardViewModel dashboardViewModel = new DashboardViewModel();

            // This makes Dashboard "View All" buttons navigate through the shell.
            // It uses NavigateTo(), the sidebar selected item also updates.
            dashboardViewModel.NavigationRequested += NavigateTo;

            // Lets the Dashboard global search jump to a patient/user record.
            dashboardViewModel.NavigationWithSearchRequested += NavigateToModuleWithSearch;

            // Lets Dashboard "View All" open a specific Reports tab.
            dashboardViewModel.NavigationToReportRequested += NavigateToReport;

            DashboardView dashboardView = new DashboardView
            {
                DataContext = dashboardViewModel
            };

            return dashboardView;
        }

        private UserManagementView CreateUserManagementView()
        {
            UserManagementViewModel userManagementViewModel = new UserManagementViewModel();

            UserManagementView userManagementView = new UserManagementView
            {
                DataContext = userManagementViewModel
            };

            return userManagementView;
        }

        private InventoryView CreateInventoryView()
        {
            InventoryViewModel inventoryViewModel = new InventoryViewModel();

            InventoryView inventoryView = new InventoryView
            {
                DataContext = inventoryViewModel
            };

            return inventoryView;
        }

        private PatientManagementView CreatePatientManagementView()
        {
            return new PatientManagementView
            {
                DataContext = new PatientManagementViewModel()
            };
        }

        private AppointmentManagementView CreateAppointmentManagementView()
        {
            AppointmentManagementViewModel appointmentViewModel = new AppointmentManagementViewModel();

            appointmentViewModel.AddPatientRequested += NavigateToPatientsAndOpenAddPatient;

            return new AppointmentManagementView
            {
                DataContext = appointmentViewModel
            };
        }

        private BillingView CreateBillingView()
        {
            return new BillingView
            {
                DataContext = new BillingViewModel()
            };
        }

        private ReportsView CreateReportsView()
        {
            return new ReportsView
            {
                DataContext = new ReportsViewModel()
            };
        }

        private HelpView CreateHelpView()
        {
            return new HelpView
            {
                DataContext = new HelpViewModel()
            };
        }

        // Navigates to the Patients or Manage Users module and pre-fills its
        // search box so the chosen record is shown immediately.
        private void NavigateToModuleWithSearch(string moduleName, string searchKey)
        {
            if (!SessionService.CanAccessModule(moduleName))
                return;

            SelectedModule = moduleName;

            switch (moduleName)
            {
                case "Patients":
                    PatientManagementViewModel patientViewModel = new();
                    patientViewModel.OpenPatientHistoryFromSearchKey(searchKey);
                    CurrentModuleView = new PatientManagementView { DataContext = patientViewModel };
                    break;

                case "ManageUsers":
                    UserManagementViewModel userViewModel = new();
                    userViewModel.OpenUserDetailsFromSearchKey(searchKey);
                    CurrentModuleView = new UserManagementView { DataContext = userViewModel };
                    break;

                default:
                    NavigateTo(moduleName);
                    break;
            }
        }

        // Opens the Reports module already switched to the requested report tab.
        private void NavigateToReport(string reportKey)
        {
            if (!SessionService.CanAccessModule("Reports"))
                return;

            SelectedModule = "Reports";

            ReportsViewModel reportsViewModel = new ReportsViewModel();
            reportsViewModel.ShowReport(reportKey);

            CurrentModuleView = new ReportsView { DataContext = reportsViewModel };
        }

        private void NavigateToPatientsAndOpenAddPatient()
        {
            SelectedModule = "Patients";

            PatientManagementViewModel patientViewModel = new PatientManagementViewModel();

            PatientManagementView patientManagementView = new PatientManagementView
            {
                DataContext = patientViewModel
            };

            CurrentModuleView = patientManagementView;

            patientViewModel.OpenAddPatientOverlayFromNavigation();
        }

        private UserControl CreatePlaceholderView(string text)
        {
            return new UserControl
            {
                Content = new TextBlock
                {
                    Text = text,
                    FontSize = 40,
                    FontWeight = System.Windows.FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };
        }

        private MaintenanceView CreateMaintenanceView()
        {
            return new MaintenanceView
            {
                DataContext = new MaintenanceViewModel()
            };
        }
    }
}
