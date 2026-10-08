# Cruz-Nery Dental Clinic Management System

## Email Notifications and Digital Receipts

The Email Notifications module sends appointment confirmations, reminders, rescheduling and cancellation notices, and PDF receipts for recorded payments. Staff can review the latest 200 tracked messages, refresh their status, retry a selected failed message, and check pending delivery. The full delivery history remains in the shared SQLite database.

Gmail setup:

1. On the computer that will send clinic emails, sign in to the clinic app as an administrator.
2. Enable Google 2-Step Verification for the clinic Gmail account and create a [Google app password](https://support.google.com/accounts/answer/185833). Use that 16-letter password, rather than the normal Gmail password. Some Google account policies do not allow app passwords.
3. Open **Email Notifications → Gmail settings**. Enter the clinic Gmail address, sender name, app password, and reminder lead time (24 hours by default).
4. Check **Enable Gmail delivery from this computer**, then save. This designates this computer as the sender for all clients using the same clinic database. The app password is protected with Windows DPAPI under the current Windows account; configure it again if the sending Windows account or computer changes.
5. Add the patient's optional email address in Add/Update Patient or migration review, and explicitly check the patient's email authorization. Existing patients start with no email address and email delivery disabled for their records. Privacy consent and active patient status are also required.
6. Use **Send pending** to check delivery immediately, or keep the app open for the automatic check every minute.

The patient database and email queue stay on the LAN. Only the designated sending computer needs internet access to Gmail (`smtp.gmail.com:587`, with required STARTTLS). Appointment and payment saving do not wait for Gmail. Each change and its notification enter the database in the same transaction. Unsent messages persist when the app closes or the connection fails; interrupted sending claims are recovered after five minutes.

Temporary delivery failures retry with increasing delays. After five unsuccessful attempts, a message becomes **Failed** and staff can use **Retry failed** after correcting the connection or settings. Authentication failures require checking the Gmail app password before retrying. A newer appointment update replaces older unsent notices; expired appointment confirmations/reminders and withdrawn patient email authorization are marked **Skipped**. **Sent** means Gmail accepted the email; it does not confirm inbox delivery or reading. The process cannot guarantee exactly-once SMTP delivery if the app stops after Gmail accepts a message but before the sent status is saved.

PDF receipt emails contain the invoice/payment snapshot captured when that payment was saved, using the existing printable receipt layout. Appointment emails contain the patient name and schedule, without clinical notes or treatment details. Recipients, payloads, and stored errors are encrypted using the clinic's existing encryption key. The Gmail app password is not stored in the shared database or its backups. Sending and reminder checks require the clinic app to remain open on the designated computer; this implementation does not install a Windows service.

Verification commands (temporary databases and simulated email transport; no real email is sent):

```powershell
dotnet run --project Tests/EmailNotifications.Checks/EmailNotifications.Checks.csproj --artifacts-path buildcheck/email-notifications
dotnet run --project Tests/PatientMigration.Checks/PatientMigration.Checks.csproj --artifacts-path buildcheck/email-migration-regression
```

## Publishing and Installer Guide

This guide explains how to publish the WPF application and package it into a Windows installer using Inno Setup.

## Requirements

Install the following before publishing:

- .NET SDK that supports `net10.0-windows`
- Inno Setup
- WebView2 Runtime on the target computer if it is not already installed

Official Inno Setup download:

```text
https://jrsoftware.org/isdl.php
```

Optional install through winget:

```powershell
winget install --id JRSoftware.InnoSetup -e -s winget -i
```

## Project Output Name and Icon

The executable name and icon are configured in `CruzNeryClinic.csproj`.

```xml
<AssemblyName>Dental Clinic Management System</AssemblyName>
<ApplicationIcon>Assets\Icons\clinic.ico</ApplicationIcon>
```

This produces:

```text
Dental Clinic Management System.exe
```

Do not manually rename only the `.exe` after publishing. Publish again after changing the project file so the `.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` stay consistent.

## Publish the Application

From the project root, run:

```powershell
dotnet publish CruzNeryClinic.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish\win-x64
```

The published application will be placed here:

```text
publish\win-x64
```

Because the executable name contains spaces, run it in PowerShell with the call operator:

```powershell
& ".\publish\win-x64\Dental Clinic Management System.exe"
```

Before building the installer, test the published app directly:

```text
Login
Dashboard loads
Patient records load
Appointments work
Billing and receipt preview work
Reports open
Backup and restore work
```

## Writable App Data

After installation, the app runs from `Program Files`, which is not writable for normal users. Runtime-generated files must be stored in app data folders.

The system currently stores generated output such as receipts and manuals under:

```text
%LOCALAPPDATA%\CruzNeryClinic\Receipts
%LOCALAPPDATA%\CruzNeryClinic\Manuals
%LOCALAPPDATA%\CruzNeryClinic\Backups
```

Do not configure generated receipts, manuals, database files, keys, or backups to write directly inside the installed app folder.

## Inno Setup Script

The installer script is located at:

```text
installer\DentalClinicManagementSystem.iss
```

Current important values:

```ini
#define MyAppName "Cruz-Nery Dental Clinic Management System"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Cruz-Nery Dental Clinic"
#define MyAppExeName "Dental Clinic Management System.exe"
```

The installer packages everything from:

```ini
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
```

This is correct. Include the entire published folder, not only the `.exe`.

The installer icon is configured with:

```ini
SetupIconFile=..\Assets\Icons\clinic.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
```

The output installer file name is:

```ini
OutputBaseFilename=CruzNeryDentalClinic-Setup-1.0.0
```

The compiled installer will be created under:

```text
installer\Output
```

## Compile the Installer

Open Inno Setup Compiler.

Open this file:

```text
installer\DentalClinicManagementSystem.iss
```

Compile using:

```text
Build > Compile
```

or press:

```text
Ctrl + F9
```

Expected output:

```text
installer\Output\CruzNeryDentalClinic-Setup-1.0.0.exe
```

## Test the Installer

After compiling, test the installer on the development computer or a clean test machine.

Check the following:

```text
Installer opens with the clinic icon
Application installs successfully
Start Menu shortcut launches the app
Desktop shortcut launches the app if selected
Login works
Receipt preview works
Manual/FAQ print preview works
Backup and restore work
Uninstall entry appears in Windows settings
```

If an installed build fails with an access denied error under `C:\Program Files`, that feature is probably still writing to the install folder instead of `%LOCALAPPDATA%`.

## Releasing an Update

Use this flow when the app is already installed and you want to ship a newer build.

First, update the version and installer output name in:

```text
installer\DentalClinicManagementSystem.iss
```

Example:

```ini
#define MyAppVersion "1.0.1"
OutputBaseFilename=CruzNeryDentalClinic-Setup-1.0.1
```

Keep the same `AppId`. Do not generate a new one for normal updates, because the same `AppId` lets Windows and Inno Setup recognize the installer as an update to the existing application.

Publish the updated application again:

```powershell
dotnet publish CruzNeryClinic.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish\win-x64
```

Test the newly published application before compiling the installer:

```powershell
& ".\publish\win-x64\Dental Clinic Management System.exe"
```

Check the main updated feature, then also quickly check login, dashboard loading, billing/receipt preview, reports, and backup/restore if those areas were affected.

Open Inno Setup Compiler, open:

```text
installer\DentalClinicManagementSystem.iss
```

Compile again using:

```text
Build > Compile
```

or:

```text
Ctrl + F9
```

The new installer should be created under:

```text
installer\Output\CruzNeryDentalClinic-Setup-1.0.1.exe
```

Run the new installer. A normal update does not require uninstalling the previous version first. The installer should overwrite the old program files while preserving app data stored under `%LOCALAPPDATA%`.

Do not delete the database unless you intentionally want a fresh system. Patient records, billing records, backups, generated receipts, keys, and other runtime data should remain outside the installed app folder.

## Notes

The temporary `*_wpftmp.csproj` file generated during WPF builds is normal. It is created by the WPF build process and should not be edited or included manually.

The `bin`, `obj`, `publish`, and `installer\Output` folders should not be committed unless a release artifact is intentionally being archived.
