using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Models;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CruzNeryClinic.ViewModels
{
    public class AppointmentManagementViewModel : BaseViewModel
    {
        #region Dependencies and Backing Fields

        private readonly AppointmentRepository appointmentRepository;
        private readonly List<AppointmentListItem> allAppointments = new();

        private int todaysAppointments;
        private int completedToday;
        private int cancelledToday;

        private string searchText = string.Empty;
        private string selectedFilterOption = "Selected Date Appointments";
        private string selectedSortOption = "Queue Priority";

        private bool isWalkInOverlayOpen;
        private bool isScheduledOverlayOpen;
        private bool isPatientSearchPopupOpen;

        private bool isAppointmentDetailsOverlayOpen;
        private AppointmentListItem? selectedAppointmentDetails;
        private string appointmentDetailsMedicalAlertText = string.Empty;
        private bool appointmentDetailsHasMedicalAlert;

        private bool isCompleteTreatmentOverlayOpen;
        private AppointmentListItem? appointmentPendingCompletion;
        private string completionTreatmentNotes = string.Empty;
        private string completionErrorMessage = string.Empty;
        private bool hasCompletionError;
        private bool isOperationDetailsOverlayOpen;
        private AppointmentListItem? appointmentPendingOperation;
        private string operationErrorMessage = string.Empty;
        private bool hasOperationError;


        // Reschedule
        private bool isRescheduleOverlayOpen;
        private AppointmentListItem? appointmentBeingRescheduled;
        private DateTime? rescheduleDate;
        private string rescheduleTimeText = string.Empty;
        private AppointmentServiceOption? selectedRescheduleService;
        private AppointmentDentistOption? selectedRescheduleDentist;
        private string rescheduleNotes = string.Empty;
        private string rescheduleErrorMessage = string.Empty;
        private bool hasRescheduleError;

        public bool IsWalkInPatientSearchPopupOpen =>
            IsPatientSearchPopupOpen && IsWalkInOverlayOpen;

        public bool IsScheduledPatientSearchPopupOpen =>
            IsPatientSearchPopupOpen && IsScheduledOverlayOpen;

        private string formTitle = string.Empty;
        private string formPatientSearchText = string.Empty;
        private AppointmentPatientSearchItem? selectedPatient;
        private AppointmentServiceOption? selectedService;
        private AppointmentDentistOption? selectedDentist;
        public event Action? AddPatientRequested;
        private AppointmentPatientMedicalAlert? selectedPatientMedicalAlert;
        private string medicalAlertText = string.Empty;
        private bool hasMedicalAlert;
        private bool hasNoPatientSearchResults;

        private DateTime displayedCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime selectedCalendarDate = DateTime.Today;

        private readonly DispatcherTimer waitingDurationTimer;

        private DateTime? formAppointmentDate = DateTime.Today;
        private string formAppointmentTimeText = string.Empty;
        private string walkInArrivalHourText = string.Empty;
        private string walkInArrivalMinuteText = string.Empty;
        private string walkInArrivalPeriod = "AM";
        private string formNotes = string.Empty;
        private string formTreatmentStage = string.Empty;
        private DateTime? formFollowUpDate;
        private string formToothNumber = string.Empty;
        private string formPrescription = string.Empty;
        private string formProphylaxisSeverity = string.Empty;
        private string formRestorationSurface = string.Empty;
        private string formRestorationDepth = string.Empty;

        private string formErrorMessage = string.Empty;
        private bool hasFormError;
        private string appointmentFormPromptMessage = string.Empty;
        private bool isAppointmentFormPromptOpen;

        private static readonly TimeSpan ClinicOpeningTime = new(10, 0, 0);
        private static readonly TimeSpan WeekdaySaturdayClosingTime = new(18, 0, 0);
        private static readonly TimeSpan SundayClosingTime = new(16, 0, 0);

        private string errorMessage = string.Empty;
        private bool hasError;

        #endregion

        #region Constructor

        public AppointmentManagementViewModel()
        {
            appointmentRepository = new AppointmentRepository();

            Appointments = new ObservableCollection<AppointmentListItem>();
            PatientSearchResults = new ObservableCollection<AppointmentPatientSearchItem>();
            ServiceOptions = new ObservableCollection<AppointmentServiceOption>();
            DentistOptions = new ObservableCollection<AppointmentDentistOption>();
            AppointmentTimeOptions = new ObservableCollection<string>();
            TimePeriodOptions = new ObservableCollection<string> { "AM", "PM" };
            TreatmentStageOptions = new ObservableCollection<string>();
            ProphylaxisSeverityOptions = new ObservableCollection<string> { "Mild", "Moderate", "Severe" };
            RestorationDepthOptions = new ObservableCollection<string> { "Shallow", "Medium", "Deep" };
            ToothOptions = new ObservableCollection<AppointmentToothOption>(
                Enumerable.Range(1, 32).Select(number => new AppointmentToothOption { ToothNumber = number }));

            SelectedServices = new ObservableCollection<AppointmentServiceOption>();
            TeethImages = new ObservableCollection<AppointmentImageItem>();
            AppointmentDetailsImages = new ObservableCollection<AppointmentImageItem>();

            waitingDurationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };

            waitingDurationTimer.Tick += (_, _) => RefreshAppointmentsView();

            FilterOptions = new ObservableCollection<string>
            {
                "Selected Date Appointments",
                "All Appointments",
                "Upcoming Scheduled",
                "Walk-in Queue",
                "Completed",
                "Cancelled",
                "No Show"
            };

            SortOptions = new ObservableCollection<string>
            {
                "Queue Priority",
                "Appointment Date/Time Ascending",
                "Appointment Date/Time Descending",
                "Patient Name A-Z",
                "Status"
            };

            OpenWalkInOverlayCommand = new RelayCommand(OpenWalkInOverlay);
            OpenScheduledOverlayCommand = new RelayCommand(OpenScheduledOverlay);

            OpenRescheduleOverlayCommand = new RelayCommand<AppointmentListItem>(OpenRescheduleOverlay);
            SaveRescheduleCommand = new RelayCommand(SaveRescheduleAppointment);
            CloseRescheduleOverlayCommand = new RelayCommand(CloseRescheduleOverlay);

            ShowAddPatientInstructionCommand = new RelayCommand(ShowAddPatientInstruction);
            SelectPatientCommand = new RelayCommand<AppointmentPatientSearchItem>(SelectPatientFromSearch);

            SaveWalkInCommand = new RelayCommand(SaveWalkInAppointment);
            SaveScheduledCommand = new RelayCommand(SaveScheduledAppointment);

            AddServiceCommand = new RelayCommand(AddSelectedServiceToList);
            RemoveServiceCommand = new RelayCommand<AppointmentServiceOption>(RemoveServiceFromList);
            ToggleToothSelectionCommand = new RelayCommand<AppointmentToothOption>(ToggleToothSelection);
            RemoveTeethImageCommand = new RelayCommand<AppointmentImageItem>(RemoveTeethImage);
            RemoveAppointmentDetailsImageCommand = new RelayCommand<AppointmentImageItem>(RemoveAppointmentDetailsImage);

            PreviousCalendarMonthCommand = new RelayCommand(GoToPreviousCalendarMonth);
            NextCalendarMonthCommand = new RelayCommand(GoToNextCalendarMonth);
            SelectCalendarDateCommand = new RelayCommand<AppointmentCalendarDay>(SelectCalendarDate);

            CancelWalkInCommand = new RelayCommand(CloseWalkInOverlay);
            CancelScheduledCommand = new RelayCommand(CloseScheduledOverlay);

            MarkArrivedCommand = new RelayCommand<AppointmentListItem>(MarkArrived);
            MarkNoShowCommand = new RelayCommand<AppointmentListItem>(MarkNoShow);
            
            StartTreatmentCommand = new RelayCommand<AppointmentListItem>(StartTreatment);
            OpenOperationDetailsCommand = new RelayCommand<AppointmentListItem>(OpenOperationDetails);
            SaveOperationDetailsCommand = new RelayCommand(SaveOperationDetails);
            CloseOperationDetailsCommand = new RelayCommand(CloseOperationDetails);
            CompleteAppointmentCommand = new RelayCommand<AppointmentListItem>(CompleteAppointment);
            CancelAppointmentCommand = new RelayCommand<AppointmentListItem>(CancelAppointment);
            ToggleUrgentCommand = new RelayCommand<AppointmentListItem>(ToggleUrgent);

            ViewAppointmentDetailsCommand = new RelayCommand<AppointmentListItem>(OpenAppointmentDetails);
            CloseAppointmentDetailsCommand = new RelayCommand(CloseAppointmentDetails);

            ConfirmCompleteAppointmentCommand = new RelayCommand(ConfirmCompleteAppointment);
            CloseCompleteTreatmentOverlayCommand = new RelayCommand(CloseCompleteTreatmentOverlay);
            CloseAppointmentFormPromptCommand = new RelayCommand(CloseAppointmentFormPrompt);

            LoadDropdownOptions();
            BuildCalendarDays();
            LoadAppointments();
            
            waitingDurationTimer.Start();
        }

        #endregion

        #region Collections

        public ObservableCollection<AppointmentListItem> Appointments { get; }

        public ObservableCollection<string> FilterOptions { get; }

        public ObservableCollection<string> SortOptions { get; }

        public ObservableCollection<AppointmentPatientSearchItem> PatientSearchResults { get; }

        public ObservableCollection<AppointmentCalendarDay> CalendarDays { get; } = new();

        public ObservableCollection<AppointmentServiceOption> ServiceOptions { get; }

        public ObservableCollection<AppointmentDentistOption> DentistOptions { get; }

        public ObservableCollection<string> AppointmentTimeOptions { get; }

        public ObservableCollection<string> TimePeriodOptions { get; }

        // Predefined choices for the walk-in arrival time (12-hour clock; AM/PM
        // is selected separately via TimePeriodOptions).
        public List<string> WalkInHourOptions { get; } =
            Enumerable.Range(1, 12).Select(h => h.ToString("00")).ToList();

        public List<string> WalkInMinuteOptions { get; } =
            Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList();

        public ObservableCollection<string> TreatmentStageOptions { get; }

        public ObservableCollection<string> ProphylaxisSeverityOptions { get; }

        public ObservableCollection<string> RestorationDepthOptions { get; }

        public ObservableCollection<AppointmentToothOption> ToothOptions { get; }

        // Services chosen for the appointment being created (combined into one
        // appointment when saved).
        public ObservableCollection<AppointmentServiceOption> SelectedServices { get; }

        // Teeth photos staged in the add overlays before the appointment is saved.
        public ObservableCollection<AppointmentImageItem> TeethImages { get; }

        // Teeth photos already stored for the appointment shown in the View
        // (Appointment Details) overlay.
        public ObservableCollection<AppointmentImageItem> AppointmentDetailsImages { get; }

        #endregion

        #region Summary Properties

        public int TodaysAppointments
        {
            get => todaysAppointments;
            set => SetProperty(ref todaysAppointments, value);
        }

        public int CompletedToday
        {
            get => completedToday;
            set => SetProperty(ref completedToday, value);
        }

        public int CancelledToday
        {
            get => cancelledToday;
            set => SetProperty(ref cancelledToday, value);
        }

        #endregion

        #region Search, Filter, Sort

        public string SearchText
        {
            get => searchText;
            set
            {
                SetProperty(ref searchText, value);
                RefreshAppointmentsView();
            }
        }

        public string SelectedFilterOption
        {
            get => selectedFilterOption;
            set
            {
                SetProperty(ref selectedFilterOption, value);
                RefreshAppointmentsView();
            }
        }

        public string SelectedSortOption
        {
            get => selectedSortOption;
            set
            {
                SetProperty(ref selectedSortOption, value);
                RefreshAppointmentsView();
            }
        }

        public DateTime DisplayedCalendarMonth
        {
            get => displayedCalendarMonth;
            set
            {
                if (SetProperty(ref displayedCalendarMonth, value))
                {
                    OnPropertyChanged(nameof(CalendarMonthTitle));
                    BuildCalendarDays();
                }
            }
        }

        public DateTime SelectedCalendarDate
        {
            get => selectedCalendarDate;
            set
            {
                if (SetProperty(ref selectedCalendarDate, value.Date))
                {
                    OnPropertyChanged(nameof(SelectedCalendarDateTitle));
                    BuildCalendarDays();
                    RefreshAppointmentsView();
                }
            }
        }

        public string CalendarMonthTitle =>
            DisplayedCalendarMonth.ToString("MMMM yyyy");

        public string SelectedCalendarDateTitle =>
            $"Appointments and Walk-in Visits for {SelectedCalendarDate:MMMM dd, yyyy}";

        #endregion

        #region Overlay Properties

        public bool IsWalkInOverlayOpen
        {
            get => isWalkInOverlayOpen;
            set
            {
                if (SetProperty(ref isWalkInOverlayOpen, value))
                {
                    OnPropertyChanged(nameof(IsWalkInPatientSearchPopupOpen));
                }
            }
        }

        public bool IsScheduledOverlayOpen
        {
            get => isScheduledOverlayOpen;
            set
            {
                if (SetProperty(ref isScheduledOverlayOpen, value))
                {
                    OnPropertyChanged(nameof(IsScheduledPatientSearchPopupOpen));
                }
            }
        }

        public bool IsRescheduleOverlayOpen
        {
            get => isRescheduleOverlayOpen;
            set => SetProperty(ref isRescheduleOverlayOpen, value);
        }

        public bool IsAppointmentDetailsOverlayOpen
        {
            get => isAppointmentDetailsOverlayOpen;
            set => SetProperty(ref isAppointmentDetailsOverlayOpen, value);
        }

        public AppointmentListItem? SelectedAppointmentDetails
        {
            get => selectedAppointmentDetails;
            set => SetProperty(ref selectedAppointmentDetails, value);
        }

        public string AppointmentDetailsMedicalAlertText
        {
            get => appointmentDetailsMedicalAlertText;
            set => SetProperty(ref appointmentDetailsMedicalAlertText, value);
        }

        public bool AppointmentDetailsHasMedicalAlert
        {
            get => appointmentDetailsHasMedicalAlert;
            set => SetProperty(ref appointmentDetailsHasMedicalAlert, value);
        }

        public bool IsCompleteTreatmentOverlayOpen
        {
            get => isCompleteTreatmentOverlayOpen;
            set => SetProperty(ref isCompleteTreatmentOverlayOpen, value);
        }

        public bool IsOperationDetailsOverlayOpen
        {
            get => isOperationDetailsOverlayOpen;
            set => SetProperty(ref isOperationDetailsOverlayOpen, value);
        }

        public AppointmentListItem? AppointmentPendingOperation
        {
            get => appointmentPendingOperation;
            set => SetProperty(ref appointmentPendingOperation, value);
        }

        public string OperationErrorMessage
        {
            get => operationErrorMessage;
            set => SetProperty(ref operationErrorMessage, value);
        }

        public bool HasOperationError
        {
            get => hasOperationError;
            set => SetProperty(ref hasOperationError, value);
        }

        public AppointmentListItem? AppointmentPendingCompletion
        {
            get => appointmentPendingCompletion;
            set => SetProperty(ref appointmentPendingCompletion, value);
        }

        public string CompletionTreatmentNotes
        {
            get => completionTreatmentNotes;
            set => SetProperty(ref completionTreatmentNotes, value);
        }

        public string CompletionErrorMessage
        {
            get => completionErrorMessage;
            set => SetProperty(ref completionErrorMessage, value);
        }

        public bool HasCompletionError
        {
            get => hasCompletionError;
            set => SetProperty(ref hasCompletionError, value);
        }


        public AppointmentListItem? AppointmentBeingRescheduled
        {
            get => appointmentBeingRescheduled;
            set
            {
                if (SetProperty(ref appointmentBeingRescheduled, value))
                {
                    OnPropertyChanged(nameof(ReschedulePatientCode));
                    OnPropertyChanged(nameof(ReschedulePatientName));
                    OnPropertyChanged(nameof(RescheduleCategory));
                    OnPropertyChanged(nameof(RescheduleAppointmentType));
                }
            }
        }

        public string ReschedulePatientCode =>
            AppointmentBeingRescheduled?.PatientCode ?? string.Empty;

        public string ReschedulePatientName =>
            AppointmentBeingRescheduled?.PatientName ?? string.Empty;

        public string RescheduleCategory =>
            AppointmentBeingRescheduled?.Category ?? string.Empty;

        public string RescheduleAppointmentType =>
            AppointmentBeingRescheduled?.AppointmentType ?? string.Empty;

        public DateTime? RescheduleDate
        {
            get => rescheduleDate;
            set
            {
                if (SetProperty(ref rescheduleDate, value))
                    RefreshRescheduleTimeOptions();
            }
        }

        public string RescheduleTimeText
        {
            get => rescheduleTimeText;
            set => SetProperty(ref rescheduleTimeText, value);
        }

        public AppointmentServiceOption? SelectedRescheduleService
        {
            get => selectedRescheduleService;
            set => SetProperty(ref selectedRescheduleService, value);
        }

        public AppointmentDentistOption? SelectedRescheduleDentist
        {
            get => selectedRescheduleDentist;
            set => SetProperty(ref selectedRescheduleDentist, value);
        }

        public string RescheduleNotes
        {
            get => rescheduleNotes;
            set => SetProperty(ref rescheduleNotes, value);
        }

        public string RescheduleErrorMessage
        {
            get => rescheduleErrorMessage;
            set => SetProperty(ref rescheduleErrorMessage, value);
        }

        public bool HasRescheduleError
        {
            get => hasRescheduleError;
            set => SetProperty(ref hasRescheduleError, value);
        }

        #endregion

        #region Form Properties

        public string FormTitle
        {
            get => formTitle;
            set => SetProperty(ref formTitle, value);
        }

        public string FormPatientSearchText
        {
            get => formPatientSearchText;
            set
            {
                SetProperty(ref formPatientSearchText, value);
                SearchPatientsForForm();
            }
        }

        public AppointmentPatientSearchItem? SelectedPatient
        {
            get => selectedPatient;
            set
            {
                if (SetProperty(ref selectedPatient, value))
                {
                    OnPropertyChanged(nameof(FormPatientCode));
                    OnPropertyChanged(nameof(FormPatientFirstName));
                    OnPropertyChanged(nameof(FormPatientLastName));
                    OnPropertyChanged(nameof(FormPatientCategory));

                    if (selectedPatient == null)
                        ClearMedicalAlert();
                }
            }
        }

        public string FormPatientCode => SelectedPatient?.PatientCode ?? string.Empty;

        public string FormPatientFirstName => SelectedPatient?.FirstName ?? string.Empty;

        public string FormPatientLastName => SelectedPatient?.LastName ?? string.Empty;

        public string FormPatientCategory => SelectedPatient?.Category ?? string.Empty;

        public bool IsPatientSearchPopupOpen
        {
            get => isPatientSearchPopupOpen;
            set
            {
                if (SetProperty(ref isPatientSearchPopupOpen, value))
                {
                    OnPropertyChanged(nameof(IsWalkInPatientSearchPopupOpen));
                    OnPropertyChanged(nameof(IsScheduledPatientSearchPopupOpen));
                }
            }
        }

        public AppointmentServiceOption? SelectedService
        {
            get => selectedService;
            set => SetProperty(ref selectedService, value);
        }

        public AppointmentDentistOption? SelectedDentist
        {
            get => selectedDentist;
            set => SetProperty(ref selectedDentist, value);
        }

        public DateTime? FormAppointmentDate
        {
            get => formAppointmentDate;
            set
            {
                if (SetProperty(ref formAppointmentDate, value))
                    RefreshAppointmentTimeOptions();
            }
        }

        public string FormAppointmentTimeText
        {
            get => formAppointmentTimeText;
            set => SetProperty(ref formAppointmentTimeText, value);
        }

        public string WalkInArrivalHourText
        {
            get => walkInArrivalHourText;
            set => SetProperty(ref walkInArrivalHourText, value);
        }

        public string WalkInArrivalMinuteText
        {
            get => walkInArrivalMinuteText;
            set => SetProperty(ref walkInArrivalMinuteText, value);
        }

        public string WalkInArrivalPeriod
        {
            get => walkInArrivalPeriod;
            set => SetProperty(ref walkInArrivalPeriod, value);
        }

        public string FormNotes
        {
            get => formNotes;
            set => SetProperty(ref formNotes, value);
        }

        public string FormTreatmentStage
        {
            get => formTreatmentStage;
            set => SetProperty(ref formTreatmentStage, value);
        }

        public DateTime? FormFollowUpDate
        {
            get => formFollowUpDate;
            set => SetProperty(ref formFollowUpDate, value);
        }

        public string FormToothNumber
        {
            get => formToothNumber;
            set => SetProperty(ref formToothNumber, value);
        }

        public string FormPrescription
        {
            get => formPrescription;
            set => SetProperty(ref formPrescription, value);
        }

        public string FormProphylaxisSeverity
        {
            get => formProphylaxisSeverity;
            set => SetProperty(ref formProphylaxisSeverity, value);
        }

        public string FormRestorationSurface
        {
            get => formRestorationSurface;
            set => SetProperty(ref formRestorationSurface, value);
        }

        public string FormRestorationDepth
        {
            get => formRestorationDepth;
            set => SetProperty(ref formRestorationDepth, value);
        }

        public bool HasTreatmentDetailSection =>
            HasDentureService ||
            HasOrthodonticsService ||
            HasExtractionService ||
            HasProphylaxisService ||
            HasRestorationService ||
            RequiresFollowUpDate ||
            RequiresTeethImages;

        public bool HasDentureService => HasSelectedService("Dentures");

        public bool HasOrthodonticsService => HasSelectedService("Orthodontics");

        public bool HasExtractionService => HasSelectedService("Extraction");

        public bool HasProphylaxisService => HasSelectedService("Prophylaxis");

        public bool HasRestorationService => HasSelectedService("Restoration / Pasta");

        public bool HasToothSelection =>
            HasExtractionService ||
            HasRestorationService ||
            HasDentureService ||
            HasOrthodonticsService ||
            HasSelectedService("Dental Crown") ||
            HasSelectedService("Fixed Bridge");

        public bool RequiresFollowUpDate =>
            HasDentureService ||
            HasOrthodonticsService ||
            HasSelectedService("Dental Crown") ||
            HasSelectedService("Fixed Bridge");

        public bool RequiresTreatmentStage => HasDentureService || HasOrthodonticsService;

        public bool RequiresTeethImages =>
            HasDentureService ||
            HasOrthodonticsService ||
            HasSelectedService("Dental Crown") ||
            HasSelectedService("Fixed Bridge");

        public string FormErrorMessage
        {
            get => formErrorMessage;
            set => SetProperty(ref formErrorMessage, value);
        }

        public bool HasFormError
        {
            get => hasFormError;
            set => SetProperty(ref hasFormError, value);
        }

        public string AppointmentFormPromptMessage
        {
            get => appointmentFormPromptMessage;
            set => SetProperty(ref appointmentFormPromptMessage, value);
        }

        public bool IsAppointmentFormPromptOpen
        {
            get => isAppointmentFormPromptOpen;
            set => SetProperty(ref isAppointmentFormPromptOpen, value);
        }

        public AppointmentPatientMedicalAlert? SelectedPatientMedicalAlert
        {
            get => selectedPatientMedicalAlert;
            set => SetProperty(ref selectedPatientMedicalAlert, value);
        }

        public string MedicalAlertText
        {
            get => medicalAlertText;
            set => SetProperty(ref medicalAlertText, value);
        }

        public bool HasMedicalAlert
        {
            get => hasMedicalAlert;
            set => SetProperty(ref hasMedicalAlert, value);
        }

        public bool HasNoPatientSearchResults
        {
            get => hasNoPatientSearchResults;
            set => SetProperty(ref hasNoPatientSearchResults, value);
        }
        #endregion

        #region Error Properties

        public string ErrorMessage
        {
            get => errorMessage;
            set => SetProperty(ref errorMessage, value);
        }

        public bool HasError
        {
            get => hasError;
            set => SetProperty(ref hasError, value);
        }

        #endregion

        #region Commands

        public ICommand OpenWalkInOverlayCommand { get; }

        public ICommand OpenScheduledOverlayCommand { get; }

        public ICommand PreviousCalendarMonthCommand { get; }

        public ICommand NextCalendarMonthCommand { get; }

        public ICommand SelectCalendarDateCommand { get; }

        public ICommand SelectPatientCommand { get; }

        public ICommand SaveWalkInCommand { get; }

        public ICommand SaveScheduledCommand { get; }

        public ICommand AddServiceCommand { get; }

        public ICommand RemoveServiceCommand { get; }

        public ICommand ToggleToothSelectionCommand { get; }

        public ICommand RemoveTeethImageCommand { get; }

        public ICommand RemoveAppointmentDetailsImageCommand { get; }

        public ICommand CancelWalkInCommand { get; }

        public ICommand CancelScheduledCommand { get; }

        public ICommand MarkArrivedCommand { get; }

        public ICommand MarkNoShowCommand { get; }

        public ICommand StartTreatmentCommand { get; }

        public ICommand OpenOperationDetailsCommand { get; }

        public ICommand SaveOperationDetailsCommand { get; }

        public ICommand CloseOperationDetailsCommand { get; }

        public ICommand CompleteAppointmentCommand { get; }

        public ICommand CancelAppointmentCommand { get; }

        public ICommand ToggleUrgentCommand { get; }

        public ICommand ShowAddPatientInstructionCommand { get; }

        public ICommand OpenRescheduleOverlayCommand { get; }

        public ICommand SaveRescheduleCommand { get; }

        public ICommand CloseRescheduleOverlayCommand { get; }

        public ICommand ViewAppointmentDetailsCommand { get; }

        public ICommand CloseAppointmentDetailsCommand { get; }

        public ICommand ConfirmCompleteAppointmentCommand { get; }

        public ICommand CloseCompleteTreatmentOverlayCommand { get; }

        public ICommand CloseAppointmentFormPromptCommand { get; }

        #endregion

        #region Load Methods

        private void LoadDropdownOptions()
        {
            LoadServices();
            LoadDentists();
        }

        private void LoadServices()
        {
            ServiceOptions.Clear();

            List<AppointmentServiceOption> services = appointmentRepository.GetActiveServices();

            foreach (AppointmentServiceOption service in services)
                ServiceOptions.Add(service);
        }

        private void LoadDentists()
        {
            DentistOptions.Clear();

            DentistOptions.Add(new AppointmentDentistOption
            {
                DentistUserId = null,
                DentistName = "Unassigned"
            });

            List<AppointmentDentistOption> dentists = appointmentRepository.GetActiveDentists();

            foreach (AppointmentDentistOption dentist in dentists)
                DentistOptions.Add(dentist);

            SelectedDentist = null;
        }

        private void LoadAppointments()
        {
            try
            {
                ClearError();

                allAppointments.Clear();

                List<AppointmentListItem> appointmentsFromDatabase = appointmentRepository.GetAppointmentListItems();

                foreach (AppointmentListItem appointment in appointmentsFromDatabase)
                    allAppointments.Add(appointment);

                RefreshSummaryCards();
                BuildCalendarDays();
                RefreshAppointmentsView();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load appointments: {ex.Message}");
            }
        }
        

        private void RefreshSummaryCards()
        {
            TodaysAppointments = appointmentRepository.GetTodayAppointmentCount();
            CompletedToday = appointmentRepository.GetCompletedTodayCount();
            CancelledToday = appointmentRepository.GetCancelledTodayCount();
        }

        #endregion

        #region Calendar Methods

        private void BuildCalendarDays()
        {
            CalendarDays.Clear();

            DateTime firstDayOfMonth = new(
                DisplayedCalendarMonth.Year,
                DisplayedCalendarMonth.Month,
                1
            );

            int daysFromSunday = (int)firstDayOfMonth.DayOfWeek;
            DateTime calendarStartDate = firstDayOfMonth.AddDays(-daysFromSunday);

            Dictionary<DateTime, int> appointmentCounts =
                appointmentRepository.GetAppointmentCountsByDate(
                    DisplayedCalendarMonth.Year,
                    DisplayedCalendarMonth.Month
                );

            for (int i = 0; i < 42; i++)
            {
                DateTime date = calendarStartDate.AddDays(i).Date;

                bool isCurrentMonth =
                    date.Month == DisplayedCalendarMonth.Month &&
                    date.Year == DisplayedCalendarMonth.Year;

                int appointmentCount = 0;

                if (isCurrentMonth && appointmentCounts.TryGetValue(date, out int count))
                    appointmentCount = count;

                CalendarDays.Add(new AppointmentCalendarDay
                {
                    Date = date,
                    AppointmentCount = appointmentCount,
                    IsCurrentMonth = isCurrentMonth,
                    IsToday = date == DateTime.Today,
                    IsSelected = date == SelectedCalendarDate.Date
                });
            }
        }

        private void GoToPreviousCalendarMonth()
        {
            DisplayedCalendarMonth = DisplayedCalendarMonth.AddMonths(-1);
        }

        private void GoToNextCalendarMonth()
        {
            DisplayedCalendarMonth = DisplayedCalendarMonth.AddMonths(1);
        }

        private void SelectCalendarDate(AppointmentCalendarDay? calendarDay)
        {
            if (calendarDay == null)
                return;

            SelectedCalendarDate = calendarDay.Date;
            SelectedFilterOption = "Selected Date Appointments";

            BuildCalendarDays();
            RefreshAppointmentsView();
        }

        #endregion

        #region Refresh View

        private void RefreshAppointmentsView()
        {
            IEnumerable<AppointmentListItem> query = allAppointments;
            DateTime today = DateTime.Today;

            query = SelectedFilterOption switch
            {
                "Selected Date Appointments" => query.Where(a => a.AppointmentDate.Date == SelectedCalendarDate.Date),
                "All Appointments" => query,
                "Upcoming Scheduled" => query.Where(a =>
                    a.AppointmentType == "Scheduled" &&
                    a.Status == "Scheduled" &&
                    a.AppointmentDate.Date >= today),
                "Walk-in Queue" => query.Where(a =>
                    a.AppointmentType == "Walk-In" &&
                    a.AppointmentDate.Date == today &&
                    a.Status is "Waiting" or "In Treatment"),
                "Completed" => query.Where(a => a.Status == "Completed"),
                "Cancelled" => query.Where(a => a.Status == "Cancelled"),
                "No Show" => query.Where(a => a.Status == "No Show"),
                _ => query.Where(a => a.AppointmentDate.Date == today)
            };

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string keyword = SearchText.Trim().ToLower();

                query = query.Where(a =>
                    a.PatientCode.ToLower().Contains(keyword) ||
                    a.PatientName.ToLower().Contains(keyword) ||
                    a.AppointmentType.ToLower().Contains(keyword) ||
                    a.Category.ToLower().Contains(keyword) ||
                    a.ServiceName.ToLower().Contains(keyword) ||
                    a.DentistName.ToLower().Contains(keyword) ||
                    a.Status.ToLower().Contains(keyword));
            }

            query = SelectedSortOption switch
            {
                "Queue Priority" => query
                    .OrderBy(a => GetQueueRank(a))
                    .ThenByDescending(a => a.WaitingMinutes)
                    .ThenBy(a => a.AppointmentDate)
                    .ThenBy(a => a.AppointmentTime)
                    .ThenBy(a => a.AppointmentId),

                "Appointment Date/Time Ascending" => query
                    .OrderBy(a => a.AppointmentDate)
                    .ThenBy(a => a.AppointmentTime),

                "Appointment Date/Time Descending" => query
                    .OrderByDescending(a => a.AppointmentDate)
                    .ThenByDescending(a => a.AppointmentTime),

                "Patient Name A-Z" => query
                    .OrderBy(a => a.PatientName),

                "Status" => query
                    .OrderBy(a => a.Status)
                    .ThenBy(a => a.AppointmentDate)
                    .ThenBy(a => a.AppointmentTime),

                _ => query
                    .OrderBy(a => GetQueueRank(a))
                    .ThenByDescending(a => a.WaitingMinutes)
                    .ThenBy(a => a.AppointmentDate)
                    .ThenBy(a => a.AppointmentTime)
            };

            List<AppointmentListItem> displayList = query.ToList();

            ApplyQueueNumbers(displayList);

            Appointments.Clear();

            foreach (AppointmentListItem appointment in displayList)
                Appointments.Add(appointment);
        }

        private int GetQueueRank(AppointmentListItem appointment)
        {
            if (appointment.Status == "In Treatment")
                return 0;

            if (appointment.IsUrgent && appointment.Status == "Waiting")
                return 1;

            if (appointment.AppointmentType == "Scheduled" &&
                appointment.Status is "Scheduled" or "Waiting")
                return 2;

            if (appointment.AppointmentType == "Walk-In" &&
                appointment.HasAgingPriority)
                return 3;

            if (appointment.AppointmentType == "Walk-In" &&
                appointment.Status == "Waiting" &&
                appointment.Category is "PWD" or "Senior")
                return 4;

            if (appointment.AppointmentType == "Walk-In" &&
                appointment.Status == "Waiting")
                return 5;

            if (appointment.Status == "Completed")
                return 6;

            if (appointment.Status == "Cancelled")
                return 7;

            if (appointment.Status == "No Show")
                return 8;

            return 9;
        }

        private void ApplyQueueNumbers(List<AppointmentListItem> displayList)
        {
            int queueNumber = 1;

            foreach (AppointmentListItem appointment in displayList)
            {
                bool isTodayActiveQueue =
                    appointment.AppointmentDate.Date == DateTime.Today &&
                    appointment.Status is "Scheduled" or "Waiting" or "In Treatment";

                appointment.QueueNumber = isTodayActiveQueue ? queueNumber++ : null;
            }
        }

        #endregion

        #region Open and Close Overlay Methods

        private void OpenWalkInOverlay()
        {
            ClearForm();
            ClearFormError();

            FormTitle = "Add Walk-in Visit";
            FormAppointmentDate = DateTime.Today;
            SetWalkInArrivalTime(DateTime.Now);
            SelectedDentist = null;

            IsWalkInOverlayOpen = true;
        }

        private void CloseWalkInOverlay()
        {
            IsPatientSearchPopupOpen = false;
            IsWalkInOverlayOpen = false;
            ClearFormError();
        }

        private void OpenScheduledOverlay()
        {
            ClearForm();
            ClearFormError();

            FormTitle = "Add Scheduled Appointment";
            FormAppointmentDate = DateTime.Today;
            FormAppointmentTimeText = GetNearestAppointmentTimeText(DateTime.Today, DateTime.Now.AddHours(1).TimeOfDay);
            SelectedDentist = null;

            IsScheduledOverlayOpen = true;
        }

        private void CloseScheduledOverlay()
        {
            IsPatientSearchPopupOpen = false;
            IsScheduledOverlayOpen = false;
            ClearFormError();
        }

        #endregion

        #region Patient Search

        private void SearchPatientsForForm()
        {
            PatientSearchResults.Clear();
            HasNoPatientSearchResults = false;

            if (string.IsNullOrWhiteSpace(FormPatientSearchText) ||
                FormPatientSearchText.Trim().Length < 2)
            {
                IsPatientSearchPopupOpen = false;
                return;
            }

            if (SelectedPatient != null &&
                FormPatientSearchText == SelectedPatient.DisplayText)
            {
                IsPatientSearchPopupOpen = false;
                return;
            }

            SelectedPatient = null;

            List<AppointmentPatientSearchItem> results =
                appointmentRepository.SearchActivePatients(FormPatientSearchText);

            foreach (AppointmentPatientSearchItem patient in results)
                PatientSearchResults.Add(patient);

            IsPatientSearchPopupOpen = PatientSearchResults.Count > 0;
            HasNoPatientSearchResults = PatientSearchResults.Count == 0;
        }
        private void SelectPatientFromSearch(AppointmentPatientSearchItem? patient)
        {
            if (patient == null)
                return;

            selectedPatient = patient;
            OnPropertyChanged(nameof(SelectedPatient));
            OnPropertyChanged(nameof(FormPatientCode));
            OnPropertyChanged(nameof(FormPatientFirstName));
            OnPropertyChanged(nameof(FormPatientLastName));
            OnPropertyChanged(nameof(FormPatientCategory));

            formPatientSearchText = patient.DisplayText;
            OnPropertyChanged(nameof(FormPatientSearchText));

            PatientSearchResults.Clear();
            IsPatientSearchPopupOpen = false;
            HasNoPatientSearchResults = false;

            LoadSelectedPatientMedicalAlert(patient.PatientId);
        }

        private void LoadSelectedPatientMedicalAlert(int patientId)
        {
            try
            {
                SelectedPatientMedicalAlert = appointmentRepository.GetPatientMedicalAlert(patientId);

                if (SelectedPatientMedicalAlert == null || !SelectedPatientMedicalAlert.HasAnyAlert)
                {
                    ClearMedicalAlert();
                    return;
                }

                MedicalAlertText = BuildMedicalAlertText(SelectedPatientMedicalAlert);
                HasMedicalAlert = true;
            }
            catch (Exception ex)
            {
                MedicalAlertText = $"Unable to load medical alert: {ex.Message}";
                HasMedicalAlert = true;
            }
        }

        private string BuildMedicalAlertText(AppointmentPatientMedicalAlert alert)
        {
            List<string> lines = new();

            if (alert.HasMedicalCondition)
                lines.Add("• Patient has a recorded medical condition.");

            if (!string.IsNullOrWhiteSpace(alert.MedicalConditionNotes))
                lines.Add($"• Medical Notes: {alert.MedicalConditionNotes}");

            if (!string.IsNullOrWhiteSpace(alert.AllergyNotes))
                lines.Add($"• Allergy Notes: {alert.AllergyNotes}");

            if (!string.IsNullOrWhiteSpace(alert.CurrentMedication))
                lines.Add($"• Current Medication: {alert.CurrentMedication}");

            if (alert.RequiresMedicalClearance)
                lines.Add("• Patient requires medical clearance.");

            if (!string.IsNullOrWhiteSpace(alert.ClearanceNotes))
                lines.Add($"• Clearance Notes: {alert.ClearanceNotes}");

            return string.Join(Environment.NewLine, lines);
        }

        private void ClearMedicalAlert()
        {
            SelectedPatientMedicalAlert = null;
            MedicalAlertText = string.Empty;
            HasMedicalAlert = false;
        }

        #endregion

        #region Save Appointment Methods

        private void AddSelectedServiceToList()
        {
            if (SelectedService == null)
                return;

            if (SelectedServices.Any(s => s.ServiceId == SelectedService.ServiceId))
            {
                SelectedService = null;
                return;
            }

            SelectedServices.Add(SelectedService);
            SelectedService = null;
            RefreshTreatmentDetailState();
        }

        private void RemoveServiceFromList(AppointmentServiceOption? service)
        {
            if (service == null)
                return;

            SelectedServices.Remove(service);
            RefreshTreatmentDetailState();
        }

        private void ToggleToothSelection(AppointmentToothOption? tooth)
        {
            if (tooth == null)
                return;

            tooth.IsSelected = !tooth.IsSelected;
            OnPropertyChanged(nameof(ToothOptions));
        }

        // Called from the overlay code-behind after the user picks an image file.
        public void AddTeethImage(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;

            if (TeethImages.Any(i => string.Equals(i.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase)))
                return;

            TeethImages.Add(new AppointmentImageItem { FilePath = sourcePath });
        }

        private void RemoveTeethImage(AppointmentImageItem? image)
        {
            if (image == null)
                return;

            TeethImages.Remove(image);
        }

        // Joins the selected services into a single display string.
        private string BuildCombinedServiceName()
        {
            return string.Join(", ", SelectedServices.Select(service => service.ServiceName));
        }

        private string BuildTreatmentDetails()
        {
            List<string> details = new();
            string treatedTeeth = BuildSelectedToothNumbersText();

            if (!string.IsNullOrWhiteSpace(treatedTeeth))
                details.Add($"Teeth involved: {treatedTeeth}");

            if (HasExtractionService)
            {
                details.Add($"Prescription / medication: {FormPrescription.Trim()}");
            }

            if (HasProphylaxisService)
                details.Add($"Prophylaxis severity: {FormProphylaxisSeverity.Trim()}");

            if (HasRestorationService)
            {
                details.Add($"Restoration surface: {FormRestorationSurface.Trim()}");
                details.Add($"Restoration depth: {FormRestorationDepth.Trim()}");
            }

            if (RequiresFollowUpDate && FormFollowUpDate.HasValue)
                details.Add($"Return date / next step: {FormFollowUpDate.Value:MM/dd/yyyy}");

            return string.Join(Environment.NewLine, details.Where(detail => !string.IsNullOrWhiteSpace(detail)));
        }

        private void RefreshTreatmentDetailState()
        {
            RefreshTreatmentStageOptions();

            OnPropertyChanged(nameof(HasTreatmentDetailSection));
            OnPropertyChanged(nameof(HasDentureService));
            OnPropertyChanged(nameof(HasOrthodonticsService));
            OnPropertyChanged(nameof(HasExtractionService));
            OnPropertyChanged(nameof(HasProphylaxisService));
            OnPropertyChanged(nameof(HasRestorationService));
            OnPropertyChanged(nameof(HasToothSelection));
            OnPropertyChanged(nameof(RequiresFollowUpDate));
            OnPropertyChanged(nameof(RequiresTreatmentStage));
            OnPropertyChanged(nameof(RequiresTeethImages));
        }

        private void RefreshTreatmentStageOptions()
        {
            string previousStage = FormTreatmentStage;
            TreatmentStageOptions.Clear();

            if (HasDentureService)
            {
                TreatmentStageOptions.Add("Impression Taking");
                TreatmentStageOptions.Add("Wax Try-In");
                TreatmentStageOptions.Add("Post Try-In Adjustment");
                TreatmentStageOptions.Add("Denture Delivery");
            }

            if (HasOrthodonticsService)
            {
                TreatmentStageOptions.Add("Initial Orthodontic Measurement");
                TreatmentStageOptions.Add("Bracket Placement");
            }

            FormTreatmentStage = TreatmentStageOptions.Contains(previousStage)
                ? previousStage
                : string.Empty;
        }

        private string BuildSelectedToothNumbersText()
        {
            return string.Join(", ",
                ToothOptions
                    .Where(tooth => tooth.IsSelected)
                    .Select(tooth => tooth.ToothNumber)
                    .OrderBy(number => number));
        }

        // Copies each staged teeth photo to storage and links it to the appointment.
        private void SaveTeethImages(int appointmentId)
        {
            foreach (AppointmentImageItem image in TeethImages)
            {
                try
                {
                    string storedPath = AppointmentImageService.SaveImage(image.FilePath);
                    appointmentRepository.AddAppointmentImage(appointmentId, storedPath);
                }
                catch
                {
                    // Skip an image that can no longer be read rather than failing
                    // the whole appointment save.
                }
            }
        }

        private void SaveWalkInAppointment()
        {
            try
            {
                ClearFormError();

                if (!ValidateAppointmentForm(isScheduled: false))
                    return;
                
                if (!ConfirmMedicalClearanceIfNeeded("add this walk-in visit"))
                    return;

                TimeSpan appointmentTime = ParseFormTime(isScheduled: false);
                DateTime appointmentDate = FormAppointmentDate!.Value.Date;

                if (!ValidateAppointmentConflict(appointmentDate, appointmentTime, isScheduled: false))
                    return;
                
                Appointment appointment = new()
                {
                    PatientId = SelectedPatient!.PatientId,
                    AppointmentType = "Walk-In",
                    Category = SelectedPatient.Category,
                    ServiceId = SelectedServices.First().ServiceId,
                    ServiceName = BuildCombinedServiceName(),
                    ServiceStage = null,
                    FollowUpDate = null,
                    TreatmentDetails = string.Empty,
                    DentistUserId = SelectedDentist!.DentistUserId,
                    DentistName = SelectedDentist.DentistName,
                    AppointmentDate = appointmentDate,
                    AppointmentTime = appointmentTime,
                    ArrivalTime = appointmentTime,
                    IsUrgent = false,
                    Priority = "Normal",
                    Status = "Waiting",
                    Notes = FormNotes.Trim()
                };

                appointmentRepository.AddAppointment(appointment);

                IsWalkInOverlayOpen = false;

                MessageBox.Show(
                    "Walk-in visit was added to the queue.",
                    "Walk-in Added",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowFormError($"Failed to save walk-in visit: {ex.Message}");
            }
        }

        private void SaveScheduledAppointment()
        {
            try
            {
                ClearFormError();

                if (!ValidateAppointmentForm(isScheduled: true))
                    return;

                if (!ConfirmMedicalClearanceIfNeeded("save this scheduled appointment"))
                    return;
    
                TimeSpan appointmentTime = ParseFormTime(isScheduled: true);
                DateTime appointmentDate = FormAppointmentDate!.Value.Date;

                if (!ValidateAppointmentConflict(appointmentDate, appointmentTime, isScheduled: true))
                    return;

                Appointment appointment = new()
                {
                    PatientId = SelectedPatient!.PatientId,
                    AppointmentType = "Scheduled",
                    Category = SelectedPatient.Category,
                    ServiceId = SelectedServices.First().ServiceId,
                    ServiceName = BuildCombinedServiceName(),
                    ServiceStage = null,
                    FollowUpDate = null,
                    TreatmentDetails = string.Empty,
                    DentistUserId = SelectedDentist!.DentistUserId,
                    DentistName = SelectedDentist.DentistName,
                    AppointmentDate = appointmentDate,
                    AppointmentTime = appointmentTime,
                    ArrivalTime = null,
                    IsUrgent = false,
                    Priority = "Scheduled",
                    Status = "Scheduled",
                    Notes = FormNotes.Trim()
                };

                appointmentRepository.AddAppointment(appointment);

                IsScheduledOverlayOpen = false;

                MessageBox.Show(
                    "Scheduled appointment was saved successfully.",
                    "Appointment Scheduled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowFormError($"Failed to save scheduled appointment: {ex.Message}");
            }
        }

        #endregion

        #region Reschedule Methods

        private void OpenRescheduleOverlay(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            if (appointment.Status != "Scheduled")
            {
                MessageBox.Show(
                    "Only scheduled appointments can be rescheduled.",
                    "Reschedule Not Allowed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            AppointmentListItem? latestAppointment =
                appointmentRepository.GetAppointmentListItemById(appointment.AppointmentId);

            if (latestAppointment == null)
            {
                ShowError("Unable to load appointment details.");
                return;
            }

            AppointmentBeingRescheduled = latestAppointment;
            RescheduleDate = latestAppointment.AppointmentDate;
            RescheduleTimeText = DateTime.Today.Add(latestAppointment.AppointmentTime).ToString("hh:mm tt");
            RescheduleNotes = latestAppointment.Notes;

            SelectedRescheduleService = ServiceOptions.FirstOrDefault(s =>
                s.ServiceId == latestAppointment.ServiceId);

            SelectedRescheduleDentist = DentistOptions.FirstOrDefault(d =>
                d.DentistUserId == latestAppointment.DentistUserId);

            if (SelectedRescheduleDentist == null)
                SelectedRescheduleDentist = DentistOptions.FirstOrDefault();

            ClearRescheduleError();
            IsRescheduleOverlayOpen = true;
        }

        private void SaveRescheduleAppointment()
        {
            if (AppointmentBeingRescheduled == null)
            {
                CloseRescheduleOverlay();
                return;
            }

            ClearRescheduleError();

            if (!ValidateRescheduleForm())
                return;

            DateTime appointmentDate = RescheduleDate!.Value.Date;
            TimeSpan appointmentTime = ParseRescheduleTime();

            if (!ValidateRescheduleConflict(
                    appointmentDate,
                    appointmentTime,
                    AppointmentBeingRescheduled.AppointmentId,
                    AppointmentBeingRescheduled.PatientId))
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Save rescheduled appointment for {AppointmentBeingRescheduled.PatientName}?",
                "Confirm Reschedule",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Appointment updatedAppointment = new()
                {
                    AppointmentId = AppointmentBeingRescheduled.AppointmentId,
                    PatientId = AppointmentBeingRescheduled.PatientId,
                    AppointmentType = "Scheduled",
                    Category = AppointmentBeingRescheduled.Category,
                    ServiceId = SelectedRescheduleService!.ServiceId,
                    ServiceName = SelectedRescheduleService.ServiceName,
                    DentistUserId = SelectedRescheduleDentist!.DentistUserId,
                    DentistName = SelectedRescheduleDentist.DentistName,
                    AppointmentDate = appointmentDate,
                    AppointmentTime = appointmentTime,
                    ArrivalTime = null,
                    IsUrgent = false,
                    Priority = "Scheduled",
                    Status = "Scheduled",
                    Notes = RescheduleNotes.Trim()
                };

                appointmentRepository.RescheduleAppointment(updatedAppointment);

                CloseRescheduleOverlay();
                LoadAppointments();

                SelectedCalendarDate = appointmentDate;
                SelectedFilterOption = "Selected Date Appointments";

                MessageBox.Show(
                    "Appointment was rescheduled successfully.",
                    "Reschedule Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                ShowRescheduleError($"Failed to reschedule appointment: {ex.Message}");
            }
        }

        private bool ValidateRescheduleForm()
        {
            if (!RescheduleDate.HasValue)
            {
                ShowRescheduleError("Please select an appointment date.");
                return false;
            }

            if (RescheduleDate.Value.Date < DateTime.Today)
            {
                ShowRescheduleError("Rescheduled appointment date cannot be in the past.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(RescheduleTimeText))
            {
                ShowRescheduleError("Please enter appointment time.");
                return false;
            }

            if (!TryParseTime(RescheduleTimeText, out TimeSpan appointmentTime))
            {
                ShowRescheduleError("Please enter a valid time. Example: 02:30 PM or 14:30.");
                return false;
            }

            DateTime appointmentDate = RescheduleDate.Value.Date;
            TimeSpan clinicClosingTime = GetClinicClosingTime(appointmentDate);

            if (appointmentTime < ClinicOpeningTime || appointmentTime > clinicClosingTime)
            {
                ShowRescheduleError($"Appointment time must be within clinic hours: {GetClinicHoursText(appointmentDate)}.");
                return false;
            }

            if (SelectedRescheduleService == null)
            {
                ShowRescheduleError("Please select a service or treatment.");
                return false;
            }

            if (SelectedRescheduleDentist == null)
            {
                ShowRescheduleError("Please select a dentist or choose Unassigned.");
                return false;
            }

            return true;
        }

        private TimeSpan ParseRescheduleTime()
        {
            TryParseTime(RescheduleTimeText, out TimeSpan time);
            return time;
        }

        private bool ValidateRescheduleConflict(
            DateTime appointmentDate,
            TimeSpan appointmentTime,
            int appointmentId,
            int patientId)
        {
            bool scheduledSlotTaken =
                appointmentRepository.HasActiveScheduledAppointmentAtExactTime(
                    appointmentDate,
                    appointmentTime,
                    ignoredAppointmentId: appointmentId
                );

            if (scheduledSlotTaken)
            {
                ShowRescheduleError("The selected schedule time is already taken. Please choose a different appointment time.");
                return false;
            }

            bool hasSameDateAppointment =
                appointmentRepository.HasSamePatientActiveAppointmentOnSameDate(
                    patientId,
                    appointmentDate,
                    appointmentTime,
                    ignoredAppointmentId: appointmentId
                );

            if (hasSameDateAppointment)
            {
                MessageBoxResult result = MessageBox.Show(
                    "This patient already has another active appointment or walk-in visit on the selected date.\n\nThis may be intentional if the patient has separate morning and afternoon visits.\n\nDo you still want to continue?",
                    "Same-Day Visit Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                    return false;
            }

            return true;
        }

        private void CloseRescheduleOverlay()
        {
            IsRescheduleOverlayOpen = false;
            AppointmentBeingRescheduled = null;
            RescheduleDate = null;
            RescheduleTimeText = string.Empty;
            SelectedRescheduleService = null;
            SelectedRescheduleDentist = null;
            RescheduleNotes = string.Empty;
            ClearRescheduleError();
        }

        private void ShowRescheduleError(string message)
        {
            RescheduleErrorMessage = message;
            HasRescheduleError = true;
        }

        private void ClearRescheduleError()
        {
            RescheduleErrorMessage = string.Empty;
            HasRescheduleError = false;
        }

        #endregion

        #region Appointment Details Methods

        private void OpenAppointmentDetails(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            try
            {
                AppointmentListItem? latestAppointment =
                    appointmentRepository.GetAppointmentListItemById(appointment.AppointmentId);

                if (latestAppointment == null)
                {
                    ShowError("Unable to load appointment details.");
                    return;
                }

                SelectedAppointmentDetails = latestAppointment;

                LoadAppointmentDetailsImages(latestAppointment.AppointmentId);

                AppointmentPatientMedicalAlert alert =
                    appointmentRepository.GetPatientMedicalAlert(latestAppointment.PatientId);

                if (alert.HasAnyAlert)
                {
                    AppointmentDetailsMedicalAlertText = BuildMedicalAlertText(alert);
                    AppointmentDetailsHasMedicalAlert = true;
                }
                else
                {
                    AppointmentDetailsMedicalAlertText = string.Empty;
                    AppointmentDetailsHasMedicalAlert = false;
                }

                IsAppointmentDetailsOverlayOpen = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load appointment details: {ex.Message}");
            }
        }

        private void CloseAppointmentDetails()
        {
            IsAppointmentDetailsOverlayOpen = false;
            SelectedAppointmentDetails = null;
            AppointmentDetailsMedicalAlertText = string.Empty;
            AppointmentDetailsHasMedicalAlert = false;
            AppointmentDetailsImages.Clear();
        }

        private void LoadAppointmentDetailsImages(int appointmentId)
        {
            AppointmentDetailsImages.Clear();

            foreach (AppointmentImageItem image in appointmentRepository.GetAppointmentImages(appointmentId))
                AppointmentDetailsImages.Add(image);
        }

        // Called from the view code-behind after the user picks image file(s) in
        // the Appointment Details overlay. Stores each photo and links it to the
        // current appointment immediately.
        public void AddAppointmentDetailsImage(string sourcePath)
        {
            int? appointmentId = SelectedAppointmentDetails?.AppointmentId
                ?? AppointmentPendingOperation?.AppointmentId;

            if (!appointmentId.HasValue || string.IsNullOrWhiteSpace(sourcePath))
                return;

            try
            {
                string storedPath = AppointmentImageService.SaveImage(sourcePath);
                appointmentRepository.AddAppointmentImage(appointmentId.Value, storedPath);
                LoadAppointmentDetailsImages(appointmentId.Value);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to upload teeth image: {ex.Message}");
            }
        }

        private void RemoveAppointmentDetailsImage(AppointmentImageItem? image)
        {
            int? appointmentId = SelectedAppointmentDetails?.AppointmentId
                ?? AppointmentPendingOperation?.AppointmentId;

            if (image == null || !appointmentId.HasValue)
                return;

            MessageBoxResult confirm = MessageBox.Show(
                "Remove this teeth image? This cannot be undone.",
                "Remove Image",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            appointmentRepository.DeleteAppointmentImage(image.AppointmentImageId);
            LoadAppointmentDetailsImages(appointmentId.Value);
        }

        #endregion

        #region Status Action Methods

        private void MarkArrived(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            MessageBoxResult result = MessageBox.Show(
                $"Mark {appointment.PatientName} as arrived and add to queue?",
                "Confirm Arrival",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                appointmentRepository.MarkArrived(appointment.AppointmentId);
                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to mark patient as arrived: {ex.Message}");
            }
        }

        private void MarkNoShow(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            MessageBoxResult result = MessageBox.Show(
                $"Mark {appointment.PatientName} as No Show?",
                "Confirm No Show",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                appointmentRepository.MarkNoShow(appointment.AppointmentId);
                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to mark appointment as no-show: {ex.Message}");
            }
        }

        private void StartTreatment(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            AppointmentPatientMedicalAlert alert =
                appointmentRepository.GetPatientMedicalAlert(appointment.PatientId);

            if (alert.RequiresMedicalClearance)
            {
                string alertText = BuildMedicalAlertText(alert);

                MessageBoxResult clearanceResult = MessageBox.Show(
                    $"This patient is marked as requiring medical clearance.\n\n{alertText}\n\nPlease confirm that clearance has been checked before starting treatment.",
                    "Medical Clearance Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (clearanceResult != MessageBoxResult.Yes)
                    return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Start treatment for {appointment.PatientName}?",
                "Start Treatment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                bool started = appointmentRepository.StartTreatment(appointment.AppointmentId);

                if (!started)
                {
                    MessageBox.Show(
                        "Another patient is currently in treatment. Complete or cancel the current treatment before starting another.",
                        "Treatment Already In Progress",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to start treatment: {ex.Message}");
            }
        }

        private void OpenOperationDetails(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            if (appointment.Status != "In Treatment")
            {
                MessageBox.Show(
                    "Operation details are only available while the appointment is in treatment.",
                    "Operation Details Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            AppointmentPendingOperation = appointmentRepository.GetAppointmentListItemById(appointment.AppointmentId) ?? appointment;
            LoadOperationFieldsFromAppointment(AppointmentPendingOperation);
            LoadAppointmentDetailsImages(AppointmentPendingOperation.AppointmentId);
            ClearOperationError();
            RefreshTreatmentDetailState();
            IsOperationDetailsOverlayOpen = true;
        }

        private void SaveOperationDetails()
        {
            if (AppointmentPendingOperation == null)
            {
                CloseOperationDetails();
                return;
            }

            ClearOperationError();

            if (!ValidateOperationDetails())
                return;

            try
            {
                appointmentRepository.UpdateOperationDetails(
                    AppointmentPendingOperation.AppointmentId,
                    RequiresTreatmentStage ? FormTreatmentStage.Trim() : null,
                    RequiresFollowUpDate ? FormFollowUpDate : null,
                    BuildTreatmentDetails()
                );

                CloseOperationDetails();
                LoadAppointments();

                MessageBox.Show(
                    "Operation details were saved successfully.",
                    "Operation Details Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                ShowOperationError($"Failed to save operation details: {ex.Message}");
            }
        }

        private void CloseOperationDetails()
        {
            IsOperationDetailsOverlayOpen = false;
            AppointmentPendingOperation = null;
            AppointmentDetailsImages.Clear();
            ClearOperationFields();
            ClearOperationError();
            RefreshTreatmentDetailState();
        }

        private void LoadOperationFieldsFromAppointment(AppointmentListItem appointment)
        {
            ClearOperationFields();

            FormTreatmentStage = appointment.ServiceStage ?? string.Empty;
            FormFollowUpDate = appointment.FollowUpDate;

            string details = appointment.TreatmentDetails ?? string.Empty;

            foreach (string rawLine in details.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();

                if (line.StartsWith("Teeth involved:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Treated teeth:", StringComparison.OrdinalIgnoreCase))
                {
                    string teethText = line
                        .Replace("Teeth involved:", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Replace("Treated teeth:", string.Empty, StringComparison.OrdinalIgnoreCase);

                    foreach (string value in teethText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(value, out int toothNumber))
                        {
                            AppointmentToothOption? tooth = ToothOptions.FirstOrDefault(option => option.ToothNumber == toothNumber);
                            if (tooth != null)
                                tooth.IsSelected = true;
                        }
                    }
                }
                else if (line.StartsWith("Prescription / medication:", StringComparison.OrdinalIgnoreCase))
                {
                    FormPrescription = line.Replace("Prescription / medication:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
                else if (line.StartsWith("Prophylaxis severity:", StringComparison.OrdinalIgnoreCase))
                {
                    FormProphylaxisSeverity = line.Replace("Prophylaxis severity:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
                else if (line.StartsWith("Restoration surface:", StringComparison.OrdinalIgnoreCase))
                {
                    FormRestorationSurface = line.Replace("Restoration surface:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
                else if (line.StartsWith("Restoration depth:", StringComparison.OrdinalIgnoreCase))
                {
                    FormRestorationDepth = line.Replace("Restoration depth:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
            }

            OnPropertyChanged(nameof(ToothOptions));
        }

        private void ClearOperationFields()
        {
            FormTreatmentStage = string.Empty;
            FormFollowUpDate = null;
            FormToothNumber = string.Empty;
            foreach (AppointmentToothOption tooth in ToothOptions)
                tooth.IsSelected = false;
            FormPrescription = string.Empty;
            FormProphylaxisSeverity = string.Empty;
            FormRestorationSurface = string.Empty;
            FormRestorationDepth = string.Empty;
            OnPropertyChanged(nameof(ToothOptions));
        }

        private void ShowOperationError(string message)
        {
            OperationErrorMessage = message;
            HasOperationError = true;
        }

        private void ClearOperationError()
        {
            OperationErrorMessage = string.Empty;
            HasOperationError = false;
        }

        private void CompleteAppointment(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            if (appointment.Status != "In Treatment")
            {
                MessageBox.Show(
                    "Only appointments that are currently in treatment can be completed.",
                    "Complete Not Allowed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            AppointmentPendingCompletion = appointment;
            CompletionTreatmentNotes = string.Empty;
            ClearCompletionError();

            IsCompleteTreatmentOverlayOpen = true;
        }

        private void ConfirmCompleteAppointment()
        {
            if (AppointmentPendingCompletion == null)
            {
                CloseCompleteTreatmentOverlay();
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Mark {AppointmentPendingCompletion.PatientName}'s appointment as completed and save treatment record?",
                "Complete Appointment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                appointmentRepository.CompleteAppointment(AppointmentPendingCompletion.AppointmentId);

                appointmentRepository.CreateTreatmentRecordFromAppointment(
                    AppointmentPendingCompletion.AppointmentId,
                    CompletionTreatmentNotes
                );

                CloseCompleteTreatmentOverlay();
                LoadAppointments();

                MessageBox.Show(
                    "Appointment was completed and treatment record was saved.",
                    "Appointment Completed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                ShowCompletionError($"Failed to complete appointment: {ex.Message}");
            }
        }

        private void CloseCompleteTreatmentOverlay()
        {
            IsCompleteTreatmentOverlayOpen = false;
            AppointmentPendingCompletion = null;
            CompletionTreatmentNotes = string.Empty;
            ClearCompletionError();
        }

        private void ShowCompletionError(string message)
        {
            CompletionErrorMessage = message;
            HasCompletionError = true;
        }

        private void ClearCompletionError()
        {
            CompletionErrorMessage = string.Empty;
            HasCompletionError = false;
        }

        private void CancelAppointment(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            MessageBoxResult result = MessageBox.Show(
                $"Cancel appointment for {appointment.PatientName}?",
                "Cancel Appointment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                appointmentRepository.CancelAppointment(appointment.AppointmentId);
                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to cancel appointment: {ex.Message}");
            }
        }

        private void ToggleUrgent(AppointmentListItem? appointment)
        {
            if (appointment == null)
                return;

            bool newUrgentValue = !appointment.IsUrgent;

            string actionText = newUrgentValue ? "mark as urgent" : "remove urgent status from";

            MessageBoxResult result = MessageBox.Show(
                $"Are you sure you want to {actionText} {appointment.PatientName}?",
                "Confirm Priority Change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                appointmentRepository.ToggleUrgent(appointment.AppointmentId, newUrgentValue);
                LoadAppointments();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to update urgent priority: {ex.Message}");
            }
        }

        #endregion

        #region Validation and Form Helpers
        private bool ValidateAppointmentForm(bool isScheduled)
        {
            if (SelectedPatient == null)
            {
                ShowFormError("Please search and select an existing patient.");
                return false;
            }

            if (SelectedServices.Count == 0)
            {
                ShowFormError("Please add at least one service or treatment.");
                return false;
            }

            if (SelectedDentist == null)
            {
                ShowFormError("Please select a dentist or choose Unassigned.");
                return false;
            }

            if (!FormAppointmentDate.HasValue)
            {
                ShowFormError("Please select an appointment date.");
                return false;
            }

            if (isScheduled && FormAppointmentDate.Value.Date < DateTime.Today)
            {
                ShowFormError("Scheduled appointment date cannot be in the past.");
                return false;
            }

            if (!isScheduled && FormAppointmentDate.Value.Date < DateTime.Today)
            {
                ShowFormError("Walk-in visit date cannot be in the past.");
                return false;
            }

            TimeSpan appointmentTime;

            if (isScheduled)
            {
                if (string.IsNullOrWhiteSpace(FormAppointmentTimeText))
                {
                    ShowFormError("Please enter appointment time.");
                    return false;
                }

                if (!TryParseTime(FormAppointmentTimeText, out appointmentTime))
                {
                    ShowFormError("Please enter a valid time. Example: 02:30 PM or 14:30.");
                    return false;
                }
            }
            else if (!TryParseWalkInArrivalTime(out appointmentTime))
            {
                ShowFormError("Please enter a valid arrival time.");
                return false;
            }

            DateTime appointmentDate = FormAppointmentDate.Value.Date;
            TimeSpan clinicClosingTime = GetClinicClosingTime(appointmentDate);

            if (appointmentTime < ClinicOpeningTime || appointmentTime > clinicClosingTime)
            {
                ShowFormError($"Appointment time must be within clinic hours: {GetClinicHoursText(appointmentDate)}.");
                return false;
            }

            return true;
        }

        private bool ValidateOperationDetails()
        {
            if (AppointmentPendingOperation == null)
                return false;

            DateTime appointmentDate = AppointmentPendingOperation.AppointmentDate.Date;

            if (RequiresTreatmentStage && string.IsNullOrWhiteSpace(FormTreatmentStage))
            {
                ShowOperationError("Please select the treatment step for denture or orthodontic work.");
                return false;
            }

            if (RequiresFollowUpDate)
            {
                if (!FormFollowUpDate.HasValue)
                {
                    ShowOperationError("Please enter the return date for the next clinic step.");
                    return false;
                }

                if (FormFollowUpDate.Value.Date < appointmentDate)
                {
                    ShowOperationError("Return date cannot be before the appointment date.");
                    return false;
                }
            }

            if (HasExtractionService)
            {
                if (!ToothOptions.Any(tooth => tooth.IsSelected))
                {
                    ShowOperationError("Please select at least one tooth number for extraction.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(FormPrescription))
                {
                    ShowOperationError("Please enter the medication or prescription for extraction.");
                    return false;
                }
            }

            if (HasProphylaxisService && string.IsNullOrWhiteSpace(FormProphylaxisSeverity))
            {
                ShowOperationError("Please select whether prophylaxis is mild, moderate, or severe.");
                return false;
            }

            if (HasRestorationService)
            {
                if (string.IsNullOrWhiteSpace(FormRestorationSurface))
                {
                    ShowOperationError("Please enter the restoration surface.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(FormRestorationDepth))
                {
                    ShowOperationError("Please select the restoration depth.");
                    return false;
                }
            }

            return true;
        }

        private bool ValidateAppointmentConflict(
            DateTime appointmentDate,
            TimeSpan appointmentTime,
            bool isScheduled)
        {
            if (SelectedPatient == null)
                return false;

            if (isScheduled)
            {
                bool scheduledSlotTaken =
                    appointmentRepository.HasActiveScheduledAppointmentAtExactTime(
                        appointmentDate,
                        appointmentTime
                    );

                if (scheduledSlotTaken)
                {
                    ShowFormError("The selected schedule time is already taken. Please choose a different appointment time.");
                    return false;
                }
            }

            bool hasSameDateAppointment =
                appointmentRepository.HasSamePatientActiveAppointmentOnSameDate(
                    SelectedPatient.PatientId,
                    appointmentDate,
                    appointmentTime
                );

            if (hasSameDateAppointment)
            {
                MessageBoxResult result = MessageBox.Show(
                    "This patient already has another active appointment or walk-in visit on the selected date.\n\nThis may be intentional if the patient has separate morning and afternoon visits.\n\nDo you still want to continue?",
                    "Same-Day Visit Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                    return false;
            }

            return true;
        }
        private TimeSpan ParseFormTime(bool isScheduled)
        {
            if (!isScheduled && TryParseWalkInArrivalTime(out TimeSpan walkInTime))
                return walkInTime;

            TryParseTime(FormAppointmentTimeText, out TimeSpan time);
            return time;
        }

        private bool TryParseWalkInArrivalTime(out TimeSpan time)
        {
            time = TimeSpan.Zero;

            if (!int.TryParse(WalkInArrivalHourText.Trim(), out int hour) ||
                !int.TryParse(WalkInArrivalMinuteText.Trim(), out int minute))
            {
                return false;
            }

            if (hour < 1 || hour > 12 || minute < 0 || minute > 59)
                return false;

            string period = (WalkInArrivalPeriod ?? string.Empty).Trim().ToUpperInvariant();

            if (period != "AM" && period != "PM")
                return false;

            int hour24 = hour % 12;

            if (period == "PM")
                hour24 += 12;

            time = new TimeSpan(hour24, minute, 0);
            FormAppointmentTimeText = DateTime.Today.Add(time).ToString("hh:mm tt");

            return true;
        }

        private void SetWalkInArrivalTime(DateTime arrivalTime)
        {
            int hour = arrivalTime.Hour % 12;
            if (hour == 0)
                hour = 12;

            WalkInArrivalHourText = hour.ToString("00");
            WalkInArrivalMinuteText = arrivalTime.Minute.ToString("00");
            WalkInArrivalPeriod = arrivalTime.Hour >= 12 ? "PM" : "AM";
            FormAppointmentTimeText = arrivalTime.ToString("hh:mm tt");
        }

        private bool TryParseTime(string value, out TimeSpan time)
        {
            time = TimeSpan.Zero;

            if (TimeSpan.TryParse(value, out time))
                return true;

            if (DateTime.TryParse(value, out DateTime dateTime))
            {
                time = dateTime.TimeOfDay;
                return true;
            }

            return false;
        }

        private TimeSpan GetClinicClosingTime(DateTime appointmentDate)
        {
            return appointmentDate.DayOfWeek == DayOfWeek.Sunday
                ? SundayClosingTime
                : WeekdaySaturdayClosingTime;
        }

        private string GetClinicHoursText(DateTime appointmentDate)
        {
            return appointmentDate.DayOfWeek == DayOfWeek.Sunday
                ? "10:00 AM to 4:00 PM on Sundays"
                : "10:00 AM to 6:00 PM from Monday to Saturday";
        }

        private void RefreshAppointmentTimeOptions()
        {
            RefreshTimeOptionsForDate(
                FormAppointmentDate,
                FormAppointmentTimeText,
                value => FormAppointmentTimeText = value
            );
        }

        private void RefreshRescheduleTimeOptions()
        {
            RefreshTimeOptionsForDate(
                RescheduleDate,
                RescheduleTimeText,
                value => RescheduleTimeText = value
            );
        }

        private void RefreshTimeOptionsForDate(
            DateTime? selectedDate,
            string currentTimeText,
            Action<string> updateCurrentTime)
        {
            AppointmentTimeOptions.Clear();

            DateTime appointmentDate = selectedDate?.Date ?? DateTime.Today;
            TimeSpan closingTime = GetClinicClosingTime(appointmentDate);

            for (TimeSpan time = ClinicOpeningTime; time <= closingTime; time = time.Add(TimeSpan.FromMinutes(15)))
                AppointmentTimeOptions.Add(DateTime.Today.Add(time).ToString("hh:mm tt"));

            if (!string.IsNullOrWhiteSpace(currentTimeText) &&
                !AppointmentTimeOptions.Contains(currentTimeText))
            {
                updateCurrentTime(AppointmentTimeOptions.FirstOrDefault() ?? string.Empty);
            }
        }

        private string GetNearestAppointmentTimeText(DateTime appointmentDate, TimeSpan preferredTime)
        {
            RefreshAppointmentTimeOptions();

            TimeSpan closingTime = GetClinicClosingTime(appointmentDate);
            TimeSpan boundedTime = preferredTime;

            if (boundedTime < ClinicOpeningTime)
                boundedTime = ClinicOpeningTime;

            if (boundedTime > closingTime)
                boundedTime = closingTime;

            int roundedMinutes = (int)(Math.Ceiling(boundedTime.TotalMinutes / 15d) * 15);
            TimeSpan roundedTime = TimeSpan.FromMinutes(roundedMinutes);

            if (roundedTime > closingTime)
                roundedTime = closingTime;

            return DateTime.Today.Add(roundedTime).ToString("hh:mm tt");
        }


        private void ClearForm()
        {
            FormPatientSearchText = string.Empty;
            SelectedPatient = null;
            PatientSearchResults.Clear();

            HasNoPatientSearchResults = false;
            IsPatientSearchPopupOpen = false;
            ClearMedicalAlert();

            SelectedService = null;
            SelectedServices.Clear();
            TeethImages.Clear();
            SelectedDentist = DentistOptions.FirstOrDefault();

            FormAppointmentDate = DateTime.Today;
            FormAppointmentTimeText = string.Empty;
            WalkInArrivalHourText = string.Empty;
            WalkInArrivalMinuteText = string.Empty;
            WalkInArrivalPeriod = "AM";
            FormTreatmentStage = string.Empty;
            FormFollowUpDate = null;
            FormToothNumber = string.Empty;
            foreach (AppointmentToothOption tooth in ToothOptions)
                tooth.IsSelected = false;
            FormPrescription = string.Empty;
            FormProphylaxisSeverity = string.Empty;
            FormRestorationSurface = string.Empty;
            FormRestorationDepth = string.Empty;
            FormNotes = string.Empty;
            RefreshTreatmentDetailState();
        }

        private bool HasSelectedService(string serviceName)
        {
            bool hasSelectedFormService = SelectedServices.Any(service =>
                string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

            if (hasSelectedFormService)
                return true;

            if (AppointmentPendingOperation == null)
                return false;

            return AppointmentPendingOperation.ServiceName
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(name => string.Equals(name, serviceName, StringComparison.OrdinalIgnoreCase));
        }

        private bool ConfirmMedicalClearanceIfNeeded(string actionText)
        {
            if (SelectedPatientMedicalAlert == null ||
                !SelectedPatientMedicalAlert.RequiresMedicalClearance)
            {
                return true;
            }

            MessageBoxResult result = MessageBox.Show(
                $"This patient is marked as requiring medical clearance.\n\n{MedicalAlertText}\n\nDo you still want to {actionText}?",
                "Medical Clearance Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            return result == MessageBoxResult.Yes;
        }

        private void ShowFormError(string message)
        {
            FormErrorMessage = string.Empty;
            HasFormError = false;
            AppointmentFormPromptMessage = message;
            IsAppointmentFormPromptOpen = true;
        }

        private void ClearFormError()
        {
            FormErrorMessage = string.Empty;
            HasFormError = false;
            AppointmentFormPromptMessage = string.Empty;
            IsAppointmentFormPromptOpen = false;
        }

        private void CloseAppointmentFormPrompt()
        {
            AppointmentFormPromptMessage = string.Empty;
            IsAppointmentFormPromptOpen = false;
        }

        #endregion

        #region General Helpers

        private void ShowError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }

        private void ClearError()
        {
            ErrorMessage = string.Empty;
            HasError = false;
        }

        private void ShowAddPatientInstruction()
        {
            MessageBoxResult result = MessageBox.Show(
                "No matching patient was found. Do you want to go to Patient Management and add a new patient?",
                "Patient Not Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            IsPatientSearchPopupOpen = false;
            IsWalkInOverlayOpen = false;
            IsScheduledOverlayOpen = false;

            AddPatientRequested?.Invoke();
        }

        #endregion
    }
}
