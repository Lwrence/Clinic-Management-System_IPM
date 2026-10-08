using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CruzNeryClinic.Models.Email;
using CruzNeryClinic.Repositories;
using CruzNeryClinic.Services;
using CruzNeryClinic.Services.Email;

namespace CruzNeryClinic.ViewModels;

public class EmailNotificationsViewModel : BaseViewModel
{
    private readonly EmailNotificationRepository repository;
    private readonly IEmailCredentialStore credentials;
    private readonly EmailDeliveryService delivery;
    private string appPassword = "", senderEmail = "", senderName = "", statusMessage = "", senderStatus = "";
    private int reminderHours = 24;
    private bool enabled, settingsOpen, busy;
    private EmailNotification? selected;
    public EmailNotificationsViewModel() : this(new(), new EmailCredentialStore(), EmailDeliveryService.Current) { }
    public EmailNotificationsViewModel(EmailNotificationRepository repository, IEmailCredentialStore credentials, EmailDeliveryService delivery)
    {
        this.repository = repository; this.credentials = credentials; this.delivery = delivery;
        RefreshCommand = new RelayCommand(Refresh, CanOperate);
        RetryCommand = new RelayCommand(Retry, () => CanOperate() && Selected?.Status == "Failed");
        SendPendingCommand = new AsyncRelayCommand(SendPendingAsync, CanOperate);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => SessionService.IsAdmin && !IsBusy);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        SaveSettingsCommand = new RelayCommand(SaveSettings, () => SessionService.IsAdmin && !IsBusy);
        Refresh();
    }
    public ObservableCollection<EmailNotification> Notifications { get; } = new();
    public bool CanConfigure => SessionService.IsAdmin;
    public ICommand RefreshCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand SendPendingCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public EmailNotification? Selected { get => selected; set { if (SetProperty(ref selected, value)) ((RelayCommand)RetryCommand).NotifyCanExecuteChanged(); } }
    public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
    public string SenderStatus { get => senderStatus; set => SetProperty(ref senderStatus, value); }
    public string SenderEmail { get => senderEmail; set => SetProperty(ref senderEmail, value); }
    public string SenderName { get => senderName; set => SetProperty(ref senderName, value); }
    public int ReminderHours { get => reminderHours; set => SetProperty(ref reminderHours, value); }
    public int[] ReminderHourOptions { get; } = { 2, 6, 12, 24, 48, 72 };
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
    public bool IsSettingsOpen { get => settingsOpen; private set => SetProperty(ref settingsOpen, value); }
    public string ThisComputer => Environment.MachineName;
    public string Summary => $"Latest {Notifications.Count} messages · {Notifications.Count(x => x.Status is "Pending" or "Sending")} pending · " +
        $"{Notifications.Count(x => x.Status == "Sent")} sent · {Notifications.Count(x => x.Status == "Failed")} failed";
    public bool IsEmpty => Notifications.Count == 0;
    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (!SetProperty(ref busy, value)) return;
            ((RelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RetryCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SendPendingCommand).NotifyCanExecuteChanged();
            ((RelayCommand)OpenSettingsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)SaveSettingsCommand).NotifyCanExecuteChanged();
        }
    }
    private bool CanOperate() => !IsBusy && SessionService.CanAccessModule("EmailNotifications");
    public void SetAppPassword(string value) => appPassword = value;
    private void Refresh()
    {
        if (!SessionService.CanAccessModule("EmailNotifications")) return;
        try
        {
            long? id = Selected?.NotificationId;
            var rows = repository.GetRecent();
            Notifications.Clear(); foreach (var row in rows) Notifications.Add(row);
            Selected = Notifications.FirstOrDefault(x => x.NotificationId == id) ?? Notifications.FirstOrDefault();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(IsEmpty));
            var settings = repository.GetSettings();
            SenderStatus = settings.Enabled ? $"Gmail sender: {settings.SenderEmail} · Delivery computer: {settings.SenderMachine}" : "Gmail delivery is disabled. Administrator setup is required.";
        }
        catch (Exception ex) { StatusMessage = $"Unable to refresh notifications: {ex.Message}"; }
    }
    private async Task SendPendingAsync()
    {
        if (!CanOperate()) return;
        IsBusy = true;
        StatusMessage = "Checking pending emails…";
        try { StatusMessage = await Task.Run(() => delivery.ProcessPendingAsync()); }
        catch (Exception) { StatusMessage = "The email queue is unavailable. Check the database connection and try again."; }
        finally { IsBusy = false; Refresh(); }
    }
    private void Retry()
    {
        if (!CanOperate() || Selected?.Status != "Failed") return;
        try { repository.RetryFailed(Selected.NotificationId); StatusMessage = "The failed message is queued for another attempt."; Refresh(); }
        catch (Exception ex) { StatusMessage = $"Unable to retry: {ex.Message}"; }
    }
    private void OpenSettings()
    {
        if (!SessionService.IsAdmin) return;
        try
        {
            var settings = repository.GetSettings();
            SenderEmail = settings.SenderEmail; SenderName = settings.SenderName;
            ReminderHours = settings.ReminderHours; Enabled = settings.Enabled;
            appPassword = ""; StatusMessage = ""; IsSettingsOpen = true;
        }
        catch (Exception ex) { StatusMessage = $"Unable to load Gmail settings: {ex.Message}"; }
    }
    private void CloseSettings() { appPassword = ""; IsSettingsOpen = false; }
    private void SaveSettings()
    {
        if (!SessionService.IsAdmin) return;
        try
        {
            if (!EmailAddressValidation.IsValid(SenderEmail)) throw new ArgumentException("Enter a valid clinic Gmail address.");
            if (string.IsNullOrWhiteSpace(SenderName)) throw new ArgumentException("Enter the sender name patients will see.");
            if (ReminderHours is < 1 or > 168) throw new ArgumentException("Select a reminder lead time between 1 and 168 hours.");
            if (!string.IsNullOrWhiteSpace(appPassword)) credentials.Save(SenderEmail, appPassword);
            if (Enabled && string.IsNullOrWhiteSpace(credentials.Read(SenderEmail)))
                throw new ArgumentException("Enter a Gmail app password for this Windows account before enabling delivery.");
            repository.SaveSettings(new() { Enabled = Enabled, SenderEmail = SenderEmail.Trim(), SenderName = SenderName.Trim(),
                SenderMachine = Environment.MachineName, ReminderHours = ReminderHours });
            CloseSettings(); Refresh();
            StatusMessage = Enabled ? "Gmail settings saved. This computer sends queued emails while the clinic app is open." : "Gmail settings saved. Delivery is disabled; queued messages are retained.";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
