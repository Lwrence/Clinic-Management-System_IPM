using CruzNeryClinic.Data;
using CruzNeryClinic.Models;
using CruzNeryClinic.Services;  
using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Collections.Generic;

namespace CruzNeryClinic.Repositories
{
    public class BillingRepository
    {
        private readonly Func<SqliteConnection> getConnection;
        private readonly Func<string?, string> encrypt, decrypt;
        private readonly Action<string, string, string> audit;
        public BillingRepository() : this(DatabaseService.GetConnection, CryptoService.EncryptString, CryptoService.DecryptString, ActivityLogService.Log) { }
        public BillingRepository(Func<SqliteConnection> connection, Func<string?, string> encrypt, Func<string?, string> decrypt, Action<string, string, string> audit)
        { getConnection = connection; this.encrypt = encrypt; this.decrypt = decrypt; this.audit = audit; }

        private string EncryptDecimal(decimal value) => encrypt(value.ToString(CultureInfo.InvariantCulture));
        private decimal DecryptDecimal(string? value, decimal fallback = 0m) =>
            decimal.TryParse(decrypt(value), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) ? amount : fallback;

        #region Appointment Payment

        public List<AppointmentPaymentItem> GetUnbilledCompletedTreatments()
        {
            List<AppointmentPaymentItem> items = new();

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
            SELECT
                tr.TreatmentRecordId,
                tr.AppointmentId,
                tr.PatientId,
                p.PatientCode,
                p.FirstName,
                p.MiddleName,
                p.LastName,
                CASE
                    WHEN COALESCE(p.IsPWD, 0) = 1 AND COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'PWD / Senior'
                    WHEN COALESCE(p.IsPWD, 0) = 1 THEN 'PWD'
                    WHEN COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'Senior Citizen'
                    ELSE 'Regular'
                END AS Category,
                tr.ServiceId,
                tr.ServiceName,
                tr.DentistName,
                tr.TreatmentDate,
                tr.TreatmentTime,
                COALESCE(s.DefaultPrice, 0) AS DefaultPrice
            FROM TreatmentRecords tr
            INNER JOIN Patients p
                ON tr.PatientId = p.PatientId
            LEFT JOIN Services s
                ON tr.ServiceId = s.ServiceId
            WHERE
                COALESCE(tr.BillingStatus, 'Unbilled') = 'Unbilled'
            ORDER BY tr.TreatmentDate DESC, tr.TreatmentTime DESC, tr.TreatmentRecordId DESC;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new AppointmentPaymentItem
                {
                    TreatmentRecordId = Convert.ToInt32(reader["TreatmentRecordId"]),
                    AppointmentId = SafeGetNullableInt(reader, "AppointmentId"),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    Category = SafeGetString(reader, "Category", "Regular"),
                    ServiceId = SafeGetNullableInt(reader, "ServiceId"),
                    ServiceName = SafeGetString(reader, "ServiceName"),
                    DentistName = SafeGetString(reader, "DentistName", "Unassigned"),
                    TreatmentDate = ParseDate(SafeGetString(reader, "TreatmentDate")),
                    TreatmentTime = ParseNullableTime(SafeGetString(reader, "TreatmentTime")),
                    DefaultPrice = Convert.ToDecimal(reader["DefaultPrice"])
                });
            }

            return items;
        }

        #endregion

        #region Balance Payment

        public List<BalancePaymentItem> GetBillingsWithBalance()
        {
            List<BalancePaymentItem> items = new();

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            bt.BillingId,
            bt.PatientId,
            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            bt.ReceiptNumber,
            bt.ServiceName,
            bt.TotalAmount,
            bt.AmountPaid,
            bt.RemainingBalance,
            bt.PaymentStatus,
            bt.TransactionDate,
            COALESCE(bt.IsArchived, 0) AS IsArchived
        FROM BillingTransactions bt
        INNER JOIN Patients p
            ON bt.PatientId = p.PatientId
        WHERE COALESCE(bt.IsArchived, 0) = 0
        ORDER BY bt.TransactionDate DESC, bt.BillingId DESC;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                decimal remainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance");
                string paymentStatus = SafeGetString(reader, "PaymentStatus");

                // Because RemainingBalance and PaymentStatus may now be encrypted,
                // filtering must happen in C# after decryption.
                if (remainingBalance <= 0)
                    continue;

                if (paymentStatus != "Unpaid" && paymentStatus != "Partial")
                    continue;

                items.Add(new BalancePaymentItem
                {
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                    ServiceName = SafeGetSecureString(reader, "ServiceName"),
                    TotalAmount = SafeGetSecureDecimal(reader, "TotalAmount"),
                    AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                    RemainingBalance = remainingBalance,
                    PaymentStatus = paymentStatus,
                    TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate"))
                });
            }

            return items;
        }

        #endregion

        #region Billing Records
        public List<BillingRecordListItem> GetBillingRecords()
        {
            List<BillingRecordListItem> items = new();

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    bt.BillingId,
    bt.PatientId,
    p.PatientCode,
    p.FirstName,
    p.MiddleName,
    p.LastName,
    bt.ReceiptNumber,
    bt.BillingSource,
    bt.ServiceName,
    bt.TotalAmount,
    bt.DiscountAmount,
    bt.SubtotalAfterDiscount,
    bt.AmountPaid,
    bt.RemainingBalance,
    bt.PaymentStatus,
    bt.TransactionDate,
    COALESCE(bt.IsArchived, 0) AS IsArchived
FROM BillingTransactions bt
INNER JOIN Patients p
    ON bt.PatientId = p.PatientId
ORDER BY bt.TransactionDate DESC, bt.BillingId DESC;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new BillingRecordListItem
                {
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                    BillingSource = SafeGetString(reader, "BillingSource"),
                    
                    ServiceName = SafeGetSecureString(reader, "ServiceName"),

                    TotalAmount = SafeGetSecureDecimal(reader, "TotalAmount"),
                    DiscountAmount = SafeGetSecureDecimal(reader, "DiscountAmount"),
                    SubtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"),
                    AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                    RemainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance"),

                    PaymentStatus = SafeGetString(reader, "PaymentStatus"),
                    TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate")),
                    IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1
                });
            }

            return items;
        }

        /// <summary>
        /// Returns the number of payments recorded today and the total cash collected today.
        /// AmountPaid is encrypted at rest, so the sum is computed in C# after decryption.
        /// </summary>
        public (int PaidTodayCount, decimal CollectedToday) GetTodayCollectionSummary()
        {
            int paidTodayCount = 0;
            decimal collectedToday = 0m;

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT AmountPaid
        FROM PaymentRecords
        WHERE date(PaymentDate) = date(@Today);";

            command.Parameters.AddWithValue("@Today", DateTime.Today.ToString("yyyy-MM-dd"));

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                paidTodayCount++;
                collectedToday += SafeGetSecureDecimal(reader, "AmountPaid");
            }

            return (paidTodayCount, collectedToday);
        }

        public List<BillingRecordListItem> GetOpenInvoicesByPatientId(int patientId)
        {
            List<BillingRecordListItem> records = new();

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            bt.BillingId,
            bt.PatientId,
            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            bt.ReceiptNumber,
            bt.BillingSource,
            bt.ServiceName,
            bt.InvoiceTitle,
            bt.TotalAmount,
            bt.DiscountAmount,
            bt.SubtotalAfterDiscount,
            bt.AmountPaid,
            bt.RemainingBalance,
            bt.PaymentStatus,
            bt.TransactionDate,
            bt.IsInvoiceOpen,
            COALESCE(bt.IsArchived, 0) AS IsArchived
        FROM BillingTransactions bt
        INNER JOIN Patients p
            ON bt.PatientId = p.PatientId
        WHERE
            bt.PatientId = @PatientId
            AND COALESCE(bt.IsInvoiceOpen, 1) = 1
            AND COALESCE(bt.IsArchived, 0) = 0
        ORDER BY bt.TransactionDate DESC, bt.BillingId DESC;";

            command.Parameters.AddWithValue("@PatientId", patientId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string invoiceTitle = SafeGetSecureString(reader, "InvoiceTitle");

                if (string.IsNullOrWhiteSpace(invoiceTitle))
                    invoiceTitle = SafeGetSecureString(reader, "ServiceName");

                records.Add(new BillingRecordListItem
                {
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                    BillingSource = SafeGetString(reader, "BillingSource"),

                    ServiceName = invoiceTitle,

                    TotalAmount = SafeGetSecureDecimal(reader, "TotalAmount"),
                    DiscountAmount = SafeGetSecureDecimal(reader, "DiscountAmount"),
                    SubtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"),
                    AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                    RemainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance"),

                    PaymentStatus = SafeGetString(reader, "PaymentStatus"),
                    TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate")),
                    IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1
                });
            }

            return records;
        }
        
        public BillingRecordListItem? GetInvoiceHeaderById(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            bt.BillingId,
            bt.PatientId,
            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            bt.ReceiptNumber,
            bt.BillingSource,
            bt.ServiceName,
            bt.InvoiceTitle,
            bt.TotalAmount,
            bt.DiscountAmount,
            bt.SubtotalAfterDiscount,
            bt.AmountPaid,
            bt.RemainingBalance,
            bt.PaymentStatus,
            bt.TransactionDate,
            bt.IsInvoiceOpen,
            COALESCE(bt.IsArchived, 0) AS IsArchived
        FROM BillingTransactions bt
        INNER JOIN Patients p
            ON bt.PatientId = p.PatientId
        WHERE bt.BillingId = @BillingId
        LIMIT 1;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            string invoiceTitle = SafeGetSecureString(reader, "InvoiceTitle");

            if (string.IsNullOrWhiteSpace(invoiceTitle))
                invoiceTitle = SafeGetSecureString(reader, "ServiceName");

            return new BillingRecordListItem
            {
                BillingId = Convert.ToInt32(reader["BillingId"]),
                PatientId = Convert.ToInt32(reader["PatientId"]),
                PatientCode = SafeGetString(reader, "PatientCode"),
                PatientName = BuildFullName(
                    SafeGetString(reader, "FirstName"),
                    SafeGetString(reader, "MiddleName"),
                    SafeGetString(reader, "LastName")
                ),
                ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                BillingSource = SafeGetString(reader, "BillingSource"),

                ServiceName = invoiceTitle,

                TotalAmount = SafeGetSecureDecimal(reader, "TotalAmount"),
                DiscountAmount = SafeGetSecureDecimal(reader, "DiscountAmount"),
                SubtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"),
                AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                RemainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance"),

                PaymentStatus = SafeGetString(reader, "PaymentStatus"),
                TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate")),
                IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1
            };
        }        
        
        public List<PaymentRecord> GetPaymentHistoryByBillingId(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();
            return GetPaymentHistoryByBillingId(connection, null, billingId);
        }

        private List<PaymentRecord> GetPaymentHistoryByBillingId(SqliteConnection connection, SqliteTransaction? transaction, int billingId)
        {
            List<PaymentRecord> payments = new();

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT
            PaymentRecordId,
            BillingId,
            PatientId,
            AmountPaid,
            PaymentMethod,
            PaymentDate,
            ReceivedByUserId,
            Notes,
            CreatedAt
        FROM PaymentRecords
        WHERE BillingId = @BillingId
        ORDER BY PaymentDate ASC, PaymentRecordId ASC;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                payments.Add(new PaymentRecord
                {
                    PaymentRecordId = Convert.ToInt32(reader["PaymentRecordId"]),
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),

                    AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                    PaymentMethod = SafeGetSecureString(reader, "PaymentMethod"),

                    PaymentDate = ParseDate(SafeGetString(reader, "PaymentDate")),
                    ReceivedByUserId = SafeGetNullableInt(reader, "ReceivedByUserId"),
                    Notes = SafeGetSecureString(reader, "Notes"),
                    CreatedAt = ParseDate(SafeGetString(reader, "CreatedAt"))
                });
            }

            return payments;
        }       
        
        #endregion

        #region Billing Receipt Details
        public BillingReceiptDetail? GetBillingReceiptDetail(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();
            return GetBillingReceiptDetail(connection, null, billingId);
        }

        private BillingReceiptDetail? GetBillingReceiptDetail(SqliteConnection connection, SqliteTransaction? transaction, int billingId)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT
            bt.BillingId,
            bt.PatientId,
            bt.AppointmentId,
            bt.TreatmentRecordId,
            bt.ReceiptNumber,
            bt.BillingSource,
            bt.ServiceName,
            bt.Description,
            bt.TotalAmount,
            bt.DiscountType,
            bt.DiscountAmount,
            bt.SubtotalAfterDiscount,
            bt.AmountPaid,
            bt.RemainingBalance,
            bt.PaymentStatus,
            bt.TransactionDate,
            bt.Notes,

            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            CASE
                WHEN COALESCE(p.IsPWD, 0) = 1 AND COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'PWD / Senior'
                WHEN COALESCE(p.IsPWD, 0) = 1 THEN 'PWD'
                WHEN COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'Senior Citizen'
                ELSE 'Regular'
            END AS PatientCategory,

            COALESCE((
                SELECT pr.PaymentMethod
                FROM PaymentRecords pr
                WHERE pr.BillingId = bt.BillingId
                ORDER BY pr.PaymentDate DESC, pr.PaymentRecordId DESC
                LIMIT 1
            ), 'Cash') AS PaymentMethod,

            (
                SELECT pr.PaymentDate
                FROM PaymentRecords pr
                WHERE pr.BillingId = bt.BillingId
                ORDER BY pr.PaymentDate DESC, pr.PaymentRecordId DESC
                LIMIT 1
            ) AS LatestPaymentDate

        FROM BillingTransactions bt
        INNER JOIN Patients p
            ON p.PatientId = bt.PatientId
        WHERE bt.BillingId = @BillingId
        LIMIT 1;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            string firstName = SafeGetString(reader, "FirstName");
            string middleName = SafeGetString(reader, "MiddleName");
            string lastName = SafeGetString(reader, "LastName");

            decimal totalAmount = SafeGetSecureDecimal(reader, "TotalAmount");
            string discountType = SafeGetString(reader, "DiscountType", "None");

            decimal actualAmountPaid = GetTotalPaidForBilling(connection, transaction, billingId);

            BillingReceiptDetail detail = new()
            {
                BillingId = Convert.ToInt32(reader["BillingId"]),
                PatientId = Convert.ToInt32(reader["PatientId"]),
                AppointmentId = SafeGetNullableInt(reader, "AppointmentId"),
                TreatmentRecordId = SafeGetNullableInt(reader, "TreatmentRecordId"),

                ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                BillingSource = SafeGetString(reader, "BillingSource"),
                PatientCode = SafeGetString(reader, "PatientCode"),
                PatientName = BuildFullName(firstName, middleName, lastName),
                PatientCategory = SafeGetString(reader, "PatientCategory", "Regular"),

                ServiceName = SafeGetSecureString(reader, "ServiceName"),
                Description = SafeGetSecureString(reader, "Description"),

                TotalAmount = totalAmount,

                VatExemptSales = discountType is "PWD" or "Senior Citizen" or "PWD/Senior"
                    ? Math.Round(totalAmount / 1.12m, 2)
                    : totalAmount,

                DiscountType = discountType,
                DiscountAmount = SafeGetSecureDecimal(reader, "DiscountAmount"),
                SubtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"),

                AmountPaid = actualAmountPaid,
                RemainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance"),
                ChangeAmount = Math.Max(actualAmountPaid - SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"), 0),

                PaymentStatus = SafeGetString(reader, "PaymentStatus"),
                PaymentMethod = SafeGetSecureString(reader, "PaymentMethod", "Cash"),

                TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate")),
                LatestPaymentDate = ParseNullableDate(SafeGetString(reader, "LatestPaymentDate")),
                Notes = SafeGetSecureString(reader, "Notes"),

                InvoiceItems = GetBillingTransactionItems(connection, transaction, billingId),
                PaymentHistory = GetPaymentHistoryByBillingId(connection, transaction, billingId)
            };
            
            return detail;
        }
        #endregion
        
        #region Billing Patient Lookup
        public List<BillingPatientLookupItem> SearchPatientsForBillingHistory(string keyword)
        {
            List<BillingPatientLookupItem> patients = new();

            if (string.IsNullOrWhiteSpace(keyword))
                return patients;

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            p.PatientId,
            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            CASE
                WHEN COALESCE(p.IsPWD, 0) = 1 AND COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'PWD / Senior'
                WHEN COALESCE(p.IsPWD, 0) = 1 THEN 'PWD'
                WHEN COALESCE(p.IsSeniorCitizen, 0) = 1 THEN 'Senior Citizen'
                ELSE 'Regular'
            END AS Category
        FROM Patients p
        WHERE
            COALESCE(p.IsActive, 1) = 1
            AND (
                p.PatientCode LIKE @Keyword
                OR p.FirstName LIKE @Keyword
                OR p.MiddleName LIKE @Keyword
                OR p.LastName LIKE @Keyword
                OR (p.LastName || ', ' || p.FirstName) LIKE @Keyword
                OR (p.FirstName || ' ' || p.LastName) LIKE @Keyword
                OR (p.FirstName || ' ' || COALESCE(p.MiddleName, '') || ' ' || p.LastName) LIKE @Keyword
            )
        ORDER BY p.LastName, p.FirstName
        LIMIT 10;";

            command.AddLikeParameter("@Keyword", keyword);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                patients.Add(new BillingPatientLookupItem
                {
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    Category = SafeGetString(reader, "Category", "Regular")
                });
            }

            return patients;
        }
        public List<BillingRecordListItem> GetBillingRecordsByPatientId(int patientId)
        {
            List<BillingRecordListItem> records = new();

            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        SELECT
            bt.BillingId,
            bt.PatientId,
            p.PatientCode,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            bt.ReceiptNumber,
            bt.BillingSource,
            bt.ServiceName,
            bt.TotalAmount,
            bt.DiscountAmount,
            bt.SubtotalAfterDiscount,
            bt.AmountPaid,
            bt.RemainingBalance,
            bt.PaymentStatus,
            bt.TransactionDate,
            COALESCE(bt.IsArchived, 0) AS IsArchived
        FROM BillingTransactions bt
        INNER JOIN Patients p
            ON bt.PatientId = p.PatientId
        WHERE bt.PatientId = @PatientId
        ORDER BY bt.TransactionDate DESC, bt.BillingId DESC;";

            command.Parameters.AddWithValue("@PatientId", patientId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                records.Add(new BillingRecordListItem
                {
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    PatientId = Convert.ToInt32(reader["PatientId"]),
                    PatientCode = SafeGetString(reader, "PatientCode"),
                    PatientName = BuildFullName(
                        SafeGetString(reader, "FirstName"),
                        SafeGetString(reader, "MiddleName"),
                        SafeGetString(reader, "LastName")
                    ),
                    ReceiptNumber = SafeGetString(reader, "ReceiptNumber"),
                    BillingSource = SafeGetString(reader, "BillingSource"),
                    
                    ServiceName = SafeGetSecureString(reader, "ServiceName"),

                    TotalAmount = SafeGetSecureDecimal(reader, "TotalAmount"),
                    DiscountAmount = SafeGetSecureDecimal(reader, "DiscountAmount"),
                    SubtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount"),
                    AmountPaid = SafeGetSecureDecimal(reader, "AmountPaid"),
                    RemainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance"),

                    PaymentStatus = SafeGetString(reader, "PaymentStatus"),
                    TransactionDate = ParseDate(SafeGetString(reader, "TransactionDate")),
                    IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1
                });
            }

            return records;
        }
        #endregion

        #region Create Billing
        public int CreateBillingTransaction(BillingTransaction billing)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO BillingTransactions (
    PatientId,
    AppointmentId,
    TreatmentRecordId,
    BillingSource,
    ReceiptNumber,
    ServiceId,
    ServiceName,
    Description,
    TotalAmount,
    DiscountType,
    DiscountAmount,
    SubtotalAfterDiscount,
    AmountPaid,
    RemainingBalance,
    PaymentStatus,
    TransactionDate,
    CreatedByUserId,
    Notes,
    CreatedAt
)
VALUES (
    @PatientId,
    @AppointmentId,
    @TreatmentRecordId,
    @BillingSource,
    @ReceiptNumber,
    @ServiceId,
    @ServiceName,
    @Description,
    @TotalAmount,
    @DiscountType,
    @DiscountAmount,
    @SubtotalAfterDiscount,
    @AmountPaid,
    @RemainingBalance,
    @PaymentStatus,
    @TransactionDate,
    @CreatedByUserId,
    @Notes,
    @CreatedAt
);

SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@PatientId", billing.PatientId);
            command.Parameters.AddWithValue("@AppointmentId", billing.AppointmentId.HasValue ? billing.AppointmentId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@TreatmentRecordId", billing.TreatmentRecordId.HasValue ? billing.TreatmentRecordId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@BillingSource", billing.BillingSource);
            command.Parameters.AddWithValue("@ReceiptNumber", billing.ReceiptNumber);
            command.Parameters.AddWithValue("@ServiceId", billing.ServiceId.HasValue ? billing.ServiceId.Value : DBNull.Value);
            
            command.Parameters.AddWithValue("@ServiceName", encrypt(billing.ServiceName));
            command.Parameters.AddWithValue("@Description", encrypt(billing.Description));

            command.Parameters.AddWithValue("@TotalAmount", EncryptDecimal(billing.TotalAmount));
            command.Parameters.AddWithValue("@DiscountType", billing.DiscountType);
            command.Parameters.AddWithValue("@DiscountAmount", EncryptDecimal(billing.DiscountAmount));
            command.Parameters.AddWithValue("@SubtotalAfterDiscount", EncryptDecimal(billing.SubtotalAfterDiscount));
            command.Parameters.AddWithValue("@AmountPaid", EncryptDecimal(billing.AmountPaid));
            command.Parameters.AddWithValue("@RemainingBalance", EncryptDecimal(billing.RemainingBalance));
            command.Parameters.AddWithValue("@PaymentStatus", billing.PaymentStatus);

            command.Parameters.AddWithValue("@TransactionDate", billing.TransactionDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("@CreatedByUserId", billing.CreatedByUserId.HasValue ? billing.CreatedByUserId.Value : DBNull.Value);
            
            command.Parameters.AddWithValue("@Notes", encrypt(billing.Notes));

            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            int newBillingId = Convert.ToInt32(command.ExecuteScalar());

            audit(
                "Create",
                "Billing",
                $"Created billing transaction (Receipt {billing.ReceiptNumber}) for '{billing.ServiceName}' totalling ₱{billing.TotalAmount:N2}");

            return newBillingId;
        }

        public int CreateInvoiceHeader(BillingTransaction invoice)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        INSERT INTO BillingTransactions (
            PatientId,
            AppointmentId,
            TreatmentRecordId,
            BillingSource,
            ReceiptNumber,
            ServiceId,
            ServiceName,
            Description,
            InvoiceTitle,
            TotalAmount,
            DiscountType,
            DiscountAmount,
            SubtotalAfterDiscount,
            AmountPaid,
            RemainingBalance,
            PaymentStatus,
            TransactionDate,
            CreatedByUserId,
            Notes,
            IsInvoiceOpen,
            CreatedAt,
            UpdatedAt
        )
        VALUES (
            @PatientId,
            @AppointmentId,
            @TreatmentRecordId,
            @BillingSource,
            @ReceiptNumber,
            @ServiceId,
            @ServiceName,
            @Description,
            @InvoiceTitle,
            @TotalAmount,
            @DiscountType,
            @DiscountAmount,
            @SubtotalAfterDiscount,
            @AmountPaid,
            @RemainingBalance,
            @PaymentStatus,
            @TransactionDate,
            @CreatedByUserId,
            @Notes,
            @IsInvoiceOpen,
            @CreatedAt,
            @UpdatedAt
        );

        SELECT last_insert_rowid();";

            string invoiceTitle = string.IsNullOrWhiteSpace(invoice.InvoiceTitle)
                ? "Dental Invoice"
                : invoice.InvoiceTitle.Trim();

            command.Parameters.AddWithValue("@PatientId", invoice.PatientId);
            command.Parameters.AddWithValue("@AppointmentId", DBNull.Value);
            command.Parameters.AddWithValue("@TreatmentRecordId", DBNull.Value);
            command.Parameters.AddWithValue("@BillingSource", invoice.BillingSource);
            command.Parameters.AddWithValue("@ReceiptNumber", invoice.ReceiptNumber);
            command.Parameters.AddWithValue("@ServiceId", DBNull.Value);

            // Existing old column. For invoice mode, use the invoice title here.
            command.Parameters.AddWithValue("@ServiceName", encrypt(invoiceTitle));
            command.Parameters.AddWithValue("@Description", encrypt(invoice.Description));
            command.Parameters.AddWithValue("@InvoiceTitle", encrypt(invoiceTitle));

            command.Parameters.AddWithValue("@TotalAmount", EncryptDecimal(0));
            command.Parameters.AddWithValue("@DiscountType", invoice.DiscountType);
            command.Parameters.AddWithValue("@DiscountAmount", EncryptDecimal(0));
            command.Parameters.AddWithValue("@SubtotalAfterDiscount", EncryptDecimal(0));
            command.Parameters.AddWithValue("@AmountPaid", EncryptDecimal(0));
            command.Parameters.AddWithValue("@RemainingBalance", EncryptDecimal(0));
            command.Parameters.AddWithValue("@PaymentStatus", "Unpaid");

            command.Parameters.AddWithValue("@TransactionDate", invoice.TransactionDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("@CreatedByUserId", invoice.CreatedByUserId.HasValue ? invoice.CreatedByUserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@Notes", encrypt(invoice.Notes));
            command.Parameters.AddWithValue("@IsInvoiceOpen", invoice.IsInvoiceOpen ? 1 : 0);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            object? result = command.ExecuteScalar();

            return Convert.ToInt32(result);
        }

        public int MarkZeroBalanceInvoicesPaidAndClosed()
        {
            int fixedCount = 0;

            using SqliteConnection connection = getConnection();
            connection.Open();

            List<int> billingIdsToFix = new();

            using (SqliteCommand selectCommand = connection.CreateCommand())
            {
                selectCommand.CommandText = @"
        SELECT
            BillingId,
            TotalAmount,
            SubtotalAfterDiscount,
            AmountPaid,
            RemainingBalance,
            PaymentStatus
        FROM BillingTransactions
        WHERE PaymentStatus IN ('Unpaid', 'Partial');";

                using SqliteDataReader reader = selectCommand.ExecuteReader();

                while (reader.Read())
                {
                    decimal totalAmount = SafeGetSecureDecimal(reader, "TotalAmount");
                    decimal subtotalAfterDiscount = SafeGetSecureDecimal(reader, "SubtotalAfterDiscount");
                    decimal amountPaid = SafeGetSecureDecimal(reader, "AmountPaid");
                    decimal remainingBalance = SafeGetSecureDecimal(reader, "RemainingBalance");

                    if (totalAmount <= 0 &&
                        subtotalAfterDiscount <= 0 &&
                        amountPaid <= 0 &&
                        remainingBalance <= 0)
                    {
                        billingIdsToFix.Add(Convert.ToInt32(reader["BillingId"]));
                    }
                }
            }

            foreach (int billingId in billingIdsToFix)
            {
                using SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = @"
        UPDATE BillingTransactions
        SET
            PaymentStatus = 'Paid',
            IsInvoiceOpen = 0,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

                updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                updateCommand.Parameters.AddWithValue("@BillingId", billingId);

                fixedCount += updateCommand.ExecuteNonQuery();
            }

            return fixedCount;
        }
        
        public int AddBillingTransactionItem(BillingTransactionItem item)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            try
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
        INSERT INTO BillingTransactionItems (
            BillingId,
            AppointmentId,
            TreatmentRecordId,
            ServiceId,
            ServiceName,
            ItemDescription,
            TreatmentDate,
            Amount,
            IsIncluded,
            CreatedAt
        )
        VALUES (
            @BillingId,
            @AppointmentId,
            @TreatmentRecordId,
            @ServiceId,
            @ServiceName,
            @ItemDescription,
            @TreatmentDate,
            @Amount,
            @IsIncluded,
            @CreatedAt
        );

        SELECT last_insert_rowid();";

                command.Parameters.AddWithValue("@BillingId", item.BillingId);
                command.Parameters.AddWithValue("@AppointmentId", item.AppointmentId.HasValue ? item.AppointmentId.Value : DBNull.Value);
                command.Parameters.AddWithValue("@TreatmentRecordId", item.TreatmentRecordId.HasValue ? item.TreatmentRecordId.Value : DBNull.Value);
                command.Parameters.AddWithValue("@ServiceId", item.ServiceId.HasValue ? item.ServiceId.Value : DBNull.Value);

                command.Parameters.AddWithValue("@ServiceName", encrypt(item.ServiceName));
                command.Parameters.AddWithValue("@ItemDescription", encrypt(item.ItemDescription));
                command.Parameters.AddWithValue("@TreatmentDate", item.TreatmentDate.HasValue ? item.TreatmentDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);

                command.Parameters.AddWithValue("@Amount", EncryptDecimal(item.IsIncluded ? 0 : item.Amount));
                command.Parameters.AddWithValue("@IsIncluded", item.IsIncluded ? 1 : 0);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                object? result = command.ExecuteScalar();
                int billingItemId = Convert.ToInt32(result);

                if (item.TreatmentRecordId.HasValue)
                {
                    MarkTreatmentRecordAsBilled(
                        connection,
                        transaction,
                        item.TreatmentRecordId.Value,
                        item.BillingId,
                        billingItemId,
                        item.IsIncluded
                    );
                }

                RecalculateInvoiceTotals(connection, transaction, item.BillingId);

                transaction.Commit();

                return billingItemId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }        
        #endregion

        #region Payments
        public void AddPaymentRecord(PaymentRecord payment)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            try
            {
                using SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = @"
        INSERT INTO PaymentRecords (
            BillingId,
            PatientId,
            AmountPaid,
            PaymentMethod,
            PaymentDate,
            ReceivedByUserId,
            Notes,
            CreatedAt
        )
        VALUES (
            @BillingId,
            @PatientId,
            @AmountPaid,
            @PaymentMethod,
            @PaymentDate,
            @ReceivedByUserId,
            @Notes,
            @CreatedAt
        ); SELECT last_insert_rowid();";

                insertCommand.Parameters.AddWithValue("@BillingId", payment.BillingId);
                insertCommand.Parameters.AddWithValue("@PatientId", payment.PatientId);
                insertCommand.Parameters.AddWithValue("@AmountPaid", EncryptDecimal(payment.AmountPaid));
                insertCommand.Parameters.AddWithValue("@PaymentMethod", encrypt(payment.PaymentMethod));
                insertCommand.Parameters.AddWithValue("@PaymentDate", payment.PaymentDate.ToString("yyyy-MM-dd"));
                insertCommand.Parameters.AddWithValue("@ReceivedByUserId", payment.ReceivedByUserId.HasValue ? payment.ReceivedByUserId.Value : DBNull.Value);
                insertCommand.Parameters.AddWithValue("@Notes", encrypt(payment.Notes));
                insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int paymentId = Convert.ToInt32(insertCommand.ExecuteScalar());

                decimal subtotalAfterDiscount = GetBillingSubtotalAfterDiscount(connection, transaction, payment.BillingId);
                decimal totalPaid = GetTotalPaidForBilling(connection, transaction, payment.BillingId);

                decimal remainingBalance = Math.Max(subtotalAfterDiscount - totalPaid, 0);

                string paymentStatus;

                if (subtotalAfterDiscount <= 0)
                    paymentStatus = "Paid";
                else if (totalPaid <= 0)
                    paymentStatus = "Unpaid";
                else if (remainingBalance <= 0)
                    paymentStatus = "Paid";
                else
                    paymentStatus = "Partial";

                using SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = @"
        UPDATE BillingTransactions
        SET
            AmountPaid = @AmountPaid,
            RemainingBalance = @RemainingBalance,
            PaymentStatus = @PaymentStatus,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

                updateCommand.Parameters.AddWithValue("@AmountPaid", EncryptDecimal(totalPaid));
                updateCommand.Parameters.AddWithValue("@RemainingBalance", EncryptDecimal(remainingBalance));
                updateCommand.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
                updateCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                updateCommand.Parameters.AddWithValue("@BillingId", payment.BillingId);

                updateCommand.ExecuteNonQuery();

                var receipt = GetBillingReceiptDetail(connection, transaction, payment.BillingId)
                    ?? throw new InvalidOperationException("The payment invoice was not found.");
                EmailNotificationRepository.QueueReceipt(connection, transaction, paymentId, receipt, encrypt, decrypt);
                transaction.Commit();

                audit(
                    "Payment",
                    "Billing",
                    $"Recorded payment of ₱{payment.AmountPaid:N2} via {payment.PaymentMethod} for billing #{payment.BillingId} (now {paymentStatus})");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        #endregion

        #region Helpers

        public string GenerateNextInvoiceNumber()
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            try
            {
                using SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.Transaction = transaction;
                selectCommand.CommandText = @"
        SELECT
            InvoicePrefix,
            NextInvoiceNumber,
            MinimumDigits,
            ApprovedSeriesEnd,
            IsBirOfficialSeries
        FROM InvoiceNumberSettings
        WHERE SettingsId = 1
        LIMIT 1;";

                using SqliteDataReader reader = selectCommand.ExecuteReader();

                if (!reader.Read())
                    throw new InvalidOperationException("Invoice number settings were not found.");

                string prefix = reader["InvoicePrefix"]?.ToString() ?? string.Empty;
                long nextNumber = Convert.ToInt64(reader["NextInvoiceNumber"]);
                int minimumDigits = Convert.ToInt32(reader["MinimumDigits"]);

                long? approvedSeriesEnd = reader["ApprovedSeriesEnd"] == DBNull.Value
                    ? null
                    : Convert.ToInt64(reader["ApprovedSeriesEnd"]);

                bool isBirOfficialSeries = Convert.ToInt32(reader["IsBirOfficialSeries"]) == 1;

                reader.Close();

                if (isBirOfficialSeries &&
                    approvedSeriesEnd.HasValue &&
                    nextNumber > approvedSeriesEnd.Value)
                {
                    throw new InvalidOperationException("The approved BIR invoice serial number range has been exhausted.");
                }

                string invoiceNumber = $"{prefix}{nextNumber.ToString().PadLeft(minimumDigits, '0')}";

                using SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = @"
        UPDATE InvoiceNumberSettings
        SET NextInvoiceNumber = @NextInvoiceNumber
        WHERE SettingsId = 1;";

                updateCommand.Parameters.AddWithValue("@NextInvoiceNumber", nextNumber + 1);
                updateCommand.ExecuteNonQuery();

                transaction.Commit();

                return invoiceNumber;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public string GenerateReceiptNumber()
        {
            return $"CN-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        public string DeterminePaymentStatus(decimal netAmount, decimal amountPaid)
        {
            if (netAmount <= 0)
                return "Paid";

            if (amountPaid <= 0)
                return "Unpaid";

            if (amountPaid >= netAmount)
                return "Paid";

            return "Partial";
        }

        public decimal CalculateBalance(decimal netAmount, decimal amountPaid)
        {
            decimal balance = netAmount - amountPaid;
            return balance < 0 ? 0 : balance;
        }

        public void ArchiveBillingRecord(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE BillingTransactions
        SET
            IsArchived = 1,
            ArchivedAt = @ArchivedAt,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.Parameters.AddWithValue("@ArchivedAt", timestamp);
            command.Parameters.AddWithValue("@UpdatedAt", timestamp);
            command.Parameters.AddWithValue("@BillingId", billingId);

            command.ExecuteNonQuery();

            audit(
                "Archive",
                "Billing",
                $"Archived billing transaction #{billingId}.");
        }

        public void RestoreBillingRecord(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE BillingTransactions
        SET
            IsArchived = 0,
            RestoredAt = @RestoredAt,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.Parameters.AddWithValue("@RestoredAt", timestamp);
            command.Parameters.AddWithValue("@UpdatedAt", timestamp);
            command.Parameters.AddWithValue("@BillingId", billingId);

            command.ExecuteNonQuery();

            audit(
                "Restore",
                "Billing",
                $"Restored billing transaction #{billingId}.");
        }

        private string BuildFullName(string firstName, string middleName, string lastName)
        {
            string middle = string.IsNullOrWhiteSpace(middleName)
                ? string.Empty
                : $" {middleName.Trim()}";

            return $"{firstName.Trim()}{middle} {lastName.Trim()}".Trim();
        }

        private string SafeGetString(SqliteDataReader reader, string columnName, string fallback = "")
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return fallback;

            return reader.GetString(ordinal);
        }

        private int? SafeGetNullableInt(SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return Convert.ToInt32(reader[columnName]);
        }

        private DateTime ParseDate(string? value)
        {
            return DateTime.TryParse(value, out DateTime date)
                ? date
                : DateTime.Today;
        }

        private DateTime? ParseNullableDate(string? value)
        {
            return DateTime.TryParse(value, out DateTime date)
                ? date
                : null;
        }

        private TimeSpan? ParseNullableTime(string? value)
        {
            return TimeSpan.TryParse(value, out TimeSpan time)
                ? time
                : null;
        }

        private static decimal SafeGetDecimal(SqliteDataReader reader, string columnName, decimal fallback = 0)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return fallback;

            return Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static string SafeGetRawString(SqliteDataReader reader, string columnName, string fallback = "")
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return fallback;

            return reader.GetValue(ordinal)?.ToString() ?? fallback;
        }

        private decimal SafeGetSecureDecimal(SqliteDataReader reader, string columnName, decimal fallback = 0m)
        {
            string rawValue = SafeGetRawString(reader, columnName);

            if (string.IsNullOrWhiteSpace(rawValue))
                return fallback;

            if (rawValue.StartsWith("ENC:", StringComparison.Ordinal))
                return DecryptDecimal(rawValue, fallback);

            if (decimal.TryParse(
                    rawValue,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal result))
            {
                return result;
            }

            return fallback;
        }

        private string SafeGetSecureString(SqliteDataReader reader, string columnName, string fallback = "")
        {
            string rawValue = SafeGetRawString(reader, columnName, fallback);
            return decrypt(rawValue);
        }

        private decimal GetBillingSubtotalAfterDiscount(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billingId)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT SubtotalAfterDiscount
        FROM BillingTransactions
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            object? value = command.ExecuteScalar();

            if (value == null || value == DBNull.Value)
                return 0m;

            string rawValue = value.ToString() ?? string.Empty;

            if (rawValue.StartsWith("ENC:", StringComparison.Ordinal))
                return DecryptDecimal(rawValue);

            if (decimal.TryParse(
                    rawValue,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal result))
            {
                return result;
            }

            return 0m;
        }

        private decimal GetTotalPaidForBilling(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            int billingId)
        {
            decimal totalPaid = 0m;

            using SqliteCommand command = connection.CreateCommand();
            
            if (transaction != null)
                command.Transaction = transaction;
            
            command.CommandText = @"
        SELECT AmountPaid
        FROM PaymentRecords
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string rawValue = reader["AmountPaid"]?.ToString() ?? string.Empty;

                if (rawValue.StartsWith("ENC:", StringComparison.Ordinal))
                {
                    totalPaid += DecryptDecimal(rawValue);
                }
                else if (decimal.TryParse(
                            rawValue,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out decimal amount))
                {
                    totalPaid += amount;
                }
            }

            return totalPaid;
        }

        private void MarkTreatmentRecordAsBilled(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int treatmentRecordId,
            int billingId,
            int billingItemId,
            bool isIncluded)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        UPDATE TreatmentRecords
        SET
            BillingStatus = @BillingStatus,
            BillingId = @BillingId,
            BillingItemId = @BillingItemId
        WHERE TreatmentRecordId = @TreatmentRecordId;";

            command.Parameters.AddWithValue("@BillingStatus", isIncluded ? "NoCharge" : "AddedToInvoice");
            command.Parameters.AddWithValue("@BillingId", billingId);
            command.Parameters.AddWithValue("@BillingItemId", billingItemId);
            command.Parameters.AddWithValue("@TreatmentRecordId", treatmentRecordId);

            command.ExecuteNonQuery();
        }

        private void RecalculateInvoiceTotals(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billingId)
        {
            decimal totalAmount = GetInvoiceItemsTotal(connection, transaction, billingId);
            decimal discountAmount = GetInvoiceDiscountAmount(connection, transaction, billingId);
            decimal subtotalAfterDiscount = Math.Max(totalAmount - discountAmount, 0);

            decimal totalPaid = GetTotalPaidForBilling(connection, transaction, billingId);
            decimal remainingBalance = Math.Max(subtotalAfterDiscount - totalPaid, 0);

            string paymentStatus;

            if (subtotalAfterDiscount <= 0)
                paymentStatus = "Paid";
            else if (totalPaid <= 0)
                paymentStatus = "Unpaid";
            else if (remainingBalance <= 0)
                paymentStatus = "Paid";
            else
                paymentStatus = "Partial";

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        UPDATE BillingTransactions
        SET
            TotalAmount = @TotalAmount,
            DiscountAmount = @DiscountAmount,
            SubtotalAfterDiscount = @SubtotalAfterDiscount,
            AmountPaid = @AmountPaid,
            RemainingBalance = @RemainingBalance,
            PaymentStatus = @PaymentStatus,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@TotalAmount", EncryptDecimal(totalAmount));
            command.Parameters.AddWithValue("@DiscountAmount", EncryptDecimal(discountAmount));
            command.Parameters.AddWithValue("@SubtotalAfterDiscount", EncryptDecimal(subtotalAfterDiscount));
            command.Parameters.AddWithValue("@AmountPaid", EncryptDecimal(totalPaid));
            command.Parameters.AddWithValue("@RemainingBalance", EncryptDecimal(remainingBalance));
            command.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@BillingId", billingId);

            command.ExecuteNonQuery();
        }

        private decimal GetInvoiceItemsTotal(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billingId)
        {
            decimal total = 0m;

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT Amount, IsIncluded
        FROM BillingTransactionItems
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                bool isIncluded = Convert.ToInt32(reader["IsIncluded"]) == 1;

                if (isIncluded)
                    continue;

                string rawAmount = reader["Amount"]?.ToString() ?? string.Empty;

                if (rawAmount.StartsWith("ENC:", StringComparison.Ordinal))
                    total += DecryptDecimal(rawAmount);
                else if (decimal.TryParse(rawAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                    total += amount;
            }

            return total;
        }

        private decimal GetInvoiceDiscountAmount(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int billingId)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT DiscountAmount
        FROM BillingTransactions
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            object? value = command.ExecuteScalar();

            if (value == null || value == DBNull.Value)
                return 0m;

            string rawValue = value.ToString() ?? string.Empty;

            if (rawValue.StartsWith("ENC:", StringComparison.Ordinal))
                return DecryptDecimal(rawValue);

            if (decimal.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
                return result;

            return 0m;
        }

        public List<BillingTransactionItem> GetBillingTransactionItems(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();
            return GetBillingTransactionItems(connection, null, billingId);
        }

        private List<BillingTransactionItem> GetBillingTransactionItems(SqliteConnection connection, SqliteTransaction? transaction, int billingId)
        {
            List<BillingTransactionItem> items = new();

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
        SELECT
            BillingItemId,
            BillingId,
            AppointmentId,
            TreatmentRecordId,
            ServiceId,
            ServiceName,
            ItemDescription,
            TreatmentDate,
            Amount,
            IsIncluded,
            CreatedAt
        FROM BillingTransactionItems
        WHERE BillingId = @BillingId
        ORDER BY
            CASE
                WHEN TreatmentDate IS NULL THEN 1
                ELSE 0
            END,
            TreatmentDate ASC,
            BillingItemId ASC;";

            command.Parameters.AddWithValue("@BillingId", billingId);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new BillingTransactionItem
                {
                    BillingItemId = Convert.ToInt32(reader["BillingItemId"]),
                    BillingId = Convert.ToInt32(reader["BillingId"]),
                    AppointmentId = SafeGetNullableInt(reader, "AppointmentId"),
                    TreatmentRecordId = SafeGetNullableInt(reader, "TreatmentRecordId"),
                    ServiceId = SafeGetNullableInt(reader, "ServiceId"),
                    ServiceName = SafeGetSecureString(reader, "ServiceName"),
                    ItemDescription = SafeGetSecureString(reader, "ItemDescription"),
                    TreatmentDate = ParseNullableDate(SafeGetString(reader, "TreatmentDate")),
                    Amount = SafeGetSecureDecimal(reader, "Amount"),
                    IsIncluded = Convert.ToInt32(reader["IsIncluded"]) == 1,
                    CreatedAt = ParseDate(SafeGetString(reader, "CreatedAt"))
                });
            }

            return items;
        }

        public void CloseInvoice(int billingId)
        {
            using SqliteConnection connection = getConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
        UPDATE BillingTransactions
        SET
            IsInvoiceOpen = 0,
            UpdatedAt = @UpdatedAt
        WHERE BillingId = @BillingId;";

            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@BillingId", billingId);

            command.ExecuteNonQuery();
        }
        #endregion
    }
}
