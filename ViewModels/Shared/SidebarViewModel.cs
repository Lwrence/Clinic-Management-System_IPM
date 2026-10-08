using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CruzNeryClinic.ViewModels.Shared
{
    // SidebarViewModel controls the reusable sidebar.
    // It displays the current user information, handles navigation requests,
    // and applies role-based access control for restricted modules.
    public class SidebarViewModel : BaseViewModel
    {
        private string selectedModule = "Dashboard";

        public event Action<string>? NavigationRequested;
        public event Action? LogoutRequested;

        public SidebarViewModel()
        {
            DashboardCommand = new RelayCommand(() => Navigate("Dashboard"));
            ManageUsersCommand = new RelayCommand(() => Navigate("ManageUsers"));
            DataMigrationCommand = new RelayCommand(() => Navigate("DataMigration"));
            EmailNotificationsCommand = new RelayCommand(() => Navigate("EmailNotifications"));
            PatientsCommand = new RelayCommand(() => Navigate("Patients"));
            AppointmentCommand = new RelayCommand(() => Navigate("Appointment"));
            BillingCommand = new RelayCommand(() => Navigate("Billing"));
            InventoryCommand = new RelayCommand(() => Navigate("Inventory"));
            MaintenanceCommand = new RelayCommand(() => Navigate("Maintenance"));
            ReportsCommand = new RelayCommand(() => Navigate("Reports"));
            HelpCommand = new RelayCommand(() => Navigate("Help"));
            LogoutCommand = new RelayCommand(Logout);
        }

        public string CurrentUserName => SessionService.GetCurrentUserDisplayName();

        public string CurrentUserCode => SessionService.GetCurrentUserCode();

        public string CurrentUserRole => SessionService.GetCurrentUserRole();

        // Admin-only modules are hidden for non-admin users.
        public bool CanAccessAdminOnlyModules => SessionService.IsAdmin;
        public bool CanAccessDataMigration => SessionService.CanAccessModule("DataMigration");

        public string SelectedModule
        {
            get => selectedModule;
            set
            {
                SetProperty(ref selectedModule, value);

                // Refresh all menu button colors after selected module changes.
                OnPropertyChanged(nameof(DashboardBackground));
                OnPropertyChanged(nameof(ManageUsersBackground));
                OnPropertyChanged(nameof(DataMigrationBackground));
                OnPropertyChanged(nameof(EmailNotificationsBackground));
                OnPropertyChanged(nameof(PatientsBackground));
                OnPropertyChanged(nameof(AppointmentBackground));
                OnPropertyChanged(nameof(BillingBackground));
                OnPropertyChanged(nameof(InventoryBackground));
                OnPropertyChanged(nameof(MaintenanceBackground));
                OnPropertyChanged(nameof(ReportsBackground));
                OnPropertyChanged(nameof(HelpBackground));


                OnPropertyChanged(nameof(DashboardForeground));
                OnPropertyChanged(nameof(ManageUsersForeground));
                OnPropertyChanged(nameof(DataMigrationForeground));
                OnPropertyChanged(nameof(EmailNotificationsForeground));
                OnPropertyChanged(nameof(PatientsForeground));
                OnPropertyChanged(nameof(AppointmentForeground));
                OnPropertyChanged(nameof(BillingForeground));
                OnPropertyChanged(nameof(InventoryForeground));
                OnPropertyChanged(nameof(MaintenanceForeground));
                OnPropertyChanged(nameof(ReportsForeground));
                OnPropertyChanged(nameof(HelpForeground));
            }
        }

        public Brush DashboardBackground => GetBackground("Dashboard");
        public Brush ManageUsersBackground => GetBackground("ManageUsers");
        public Brush DataMigrationBackground => GetBackground("DataMigration");
        public Brush EmailNotificationsBackground => GetBackground("EmailNotifications");
        public Brush PatientsBackground => GetBackground("Patients");
        public Brush AppointmentBackground => GetBackground("Appointment");
        public Brush BillingBackground => GetBackground("Billing");
        public Brush InventoryBackground => GetBackground("Inventory");
        public Brush MaintenanceBackground => GetBackground("Maintenance");
        public Brush ReportsBackground => GetBackground("Reports");
        public Brush HelpBackground => GetBackground("Help");

        public Brush DashboardForeground => GetForeground("Dashboard");
        public Brush ManageUsersForeground => GetForeground("ManageUsers");
        public Brush DataMigrationForeground => GetForeground("DataMigration");
        public Brush EmailNotificationsForeground => GetForeground("EmailNotifications");
        public Brush PatientsForeground => GetForeground("Patients");
        public Brush AppointmentForeground => GetForeground("Appointment");
        public Brush BillingForeground => GetForeground("Billing");
        public Brush InventoryForeground => GetForeground("Inventory");
        public Brush MaintenanceForeground => GetForeground("Maintenance");
        public Brush ReportsForeground => GetForeground("Reports");
        public Brush HelpForeground => GetForeground("Help");

        public ICommand DashboardCommand { get; }
        public ICommand ManageUsersCommand { get; }
        public ICommand DataMigrationCommand { get; }
        public ICommand EmailNotificationsCommand { get; }
        public ICommand PatientsCommand { get; }
        public ICommand AppointmentCommand { get; }
        public ICommand BillingCommand { get; }
        public ICommand InventoryCommand { get; }
        public ICommand MaintenanceCommand { get; }
        public ICommand ReportsCommand { get; }
        public ICommand HelpCommand { get; }
        public ICommand LogoutCommand { get; }

        private Brush GetBackground(string moduleName)
        {
            if (SelectedModule == moduleName)
                return new SolidColorBrush(Color.FromRgb(230, 252, 255));

            return Brushes.Transparent;
        }

        private Brush GetForeground(string moduleName)
        {
            if (SelectedModule == moduleName)
                return Brushes.Black;

            return Brushes.White;
        }

        private void Navigate(string moduleName)
        {
            if (!SessionService.CanAccessModule(moduleName))
                return;

            SelectedModule = moduleName;
            NavigationRequested?.Invoke(moduleName);
        }

        private void Logout()
        {
            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to log out?",
                "Confirm Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            SessionService.Logout();
            LogoutRequested?.Invoke();
        }
    }
}
