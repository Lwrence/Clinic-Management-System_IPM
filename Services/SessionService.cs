using CruzNeryClinic.Models;

namespace CruzNeryClinic.Services
{
    // SessionService stores the currently logged-in user while the app is running.
    // This helps the system know who is using the system and what role they have.
    public static class SessionService
    {
        // Stores the current logged-in user.
        // If this is null, it means no user is logged in.
        public static User? CurrentUser { get; private set; }

        // Returns true if someone is currently logged in.
        public static bool IsLoggedIn => CurrentUser != null;

        // Returns true if the logged-in user is an Admin.
        // This will be used for role-based access later.
        public static bool IsAdmin => CurrentUser?.IsAdmin == true;

        // Saves the user session after successful login.
        public static void Login(User user)
        {
            CurrentUser = user;
        }

        // Clears the user session during logout.
        public static void Logout()
        {
            CurrentUser = null;
        }

        // Sidebar display format based on your UI:
        // Example: Barredo, Augustine L.
        public static string GetCurrentUserDisplayName()
        {
            if (CurrentUser == null)
                return string.Empty;

            string middleInitial = string.IsNullOrWhiteSpace(CurrentUser.MiddleName)
                ? string.Empty
                : $" {CurrentUser.MiddleName[0]}.";

            return $"{CurrentUser.LastName}, {CurrentUser.FirstName}{middleInitial}";
        }

        public static string GetCurrentUserFullName()
        {
            return CurrentUser?.FullName ?? string.Empty;
        }

        public static string GetCurrentUserCode()
        {
            return CurrentUser?.UserCode ?? string.Empty;
        }

        public static string GetCurrentUserRole()
        {
            return CurrentUser?.Role ?? string.Empty;
        }

        // Central permission checker.
        // Admin can access everything.
        // Other roles cannot access Manage Users, Maintenance, and Reports.
        public static bool CanAccessModule(string moduleName)
        {
            if (!IsLoggedIn)
                return false;

            if (IsAdmin)
                return true;

            return moduleName switch
            {
                "ManageUsers" => false,
                "DataMigration" => true,
                "Maintenance" => false,
                "Reports" => false,
                _ => true
            };
        }
    }
}
