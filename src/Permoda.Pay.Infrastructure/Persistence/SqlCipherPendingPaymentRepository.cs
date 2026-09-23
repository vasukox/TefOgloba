using System.Data;
using Microsoft.Data.Sqlite;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Security;

namespace Permoda.Pay.Infrastructure.Persistence;

public sealed class SqlCipherPendingPaymentRepository : IPendingPaymentRepository
{
    private const int SchemaVersion = 1;

    private static readonly Lazy<bool> SqlCipherInitialization = new(() =>
    {
        SQLitePCL.Batteries_V2.Init();
        return true;
    });

    private readonly string _databasePath;
    private readonly IDatabaseEncryptionKeyProvider _keyProvider;
    private readonly TimeProvider _timeProvider;

    public SqlCipherPendingPaymentRepository(
        string databasePath,
        IDatabaseEncryptionKeyProvider keyProvider,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path is required.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _keyProvider = keyProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PortResult<Unit>> SaveAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return ExecuteAsync(
            async (connection, token) =>
            {
                var state = transaction.ToState();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO PendingPayments (
                        TransactionNumber,
                        StoreId,
                        TerminalId,
                        CashierId,
                        CardValue,
                        CardKind,
                        Amount,
                        Currency,
                        Operation,
                        ReferenceNumber,
                        Status,
                        ReconciliationStatus,
                        UpdatedAtUtc)
                    VALUES (
                        $transactionNumber,
                        $storeId,
                        $terminalId,
                        $cashierId,
                        $cardValue,
                        $cardKind,
                        $amount,
                        $currency,
                        $operation,
                        $referenceNumber,
                        $status,
                        $reconciliationStatus,
                        $updatedAtUtc)
                    ON CONFLICT(TransactionNumber) DO UPDATE SET
                        StoreId = excluded.StoreId,
                        TerminalId = excluded.TerminalId,
                        CashierId = excluded.CashierId,
                        CardValue = excluded.CardValue,
                        CardKind = excluded.CardKind,
                        Amount = excluded.Amount,
                        Currency = excluded.Currency,
                        Operation = excluded.Operation,
                        ReferenceNumber = excluded.ReferenceNumber,
                        Status = excluded.Status,
                        ReconciliationStatus = excluded.ReconciliationStatus,
                        UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$transactionNumber", state.TransactionNumber.Value);
                command.Parameters.AddWithValue("$storeId", state.StoreId.Value);
                command.Parameters.AddWithValue("$terminalId", state.TerminalId.Value);
                command.Parameters.AddWithValue("$cashierId", state.CashierId.Value);
                // PAN enmascarado en storage (113817******5937). El serial completo del bono
                // nunca toca disco: la recuperación tras crash solo necesita el referenceNumber
                // (de /confirmTransaction, /reversal, etc.) — no requiere volver a enviar el
                // cardNumber. Si en algún flujo futuro se necesitara el PAN crudo, se reabre
                // este punto con un análisis de riesgo y rotación de claves.
                command.Parameters.AddWithValue("$cardValue", state.CardIdentifier.MaskedValue);
                command.Parameters.AddWithValue("$cardKind", (int)state.CardIdentifier.Kind);
                command.Parameters.AddWithValue("$amount", state.Amount.MinorUnits);
                command.Parameters.AddWithValue("$currency", state.Amount.Currency);
                command.Parameters.AddWithValue("$operation", (int)state.Operation);
                command.Parameters.AddWithValue(
                    "$referenceNumber",
                    state.ReferenceNumber?.Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$status", (int)state.Status);
                command.Parameters.AddWithValue(
                    "$reconciliationStatus",
                    (int)state.ReconciliationStatus);
                command.Parameters.AddWithValue(
                    "$updatedAtUtc",
                    _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                await command.ExecuteNonQueryAsync(token);

                return Unit.Value;
            },
            cancellationToken);
    }

    public Task<PortResult<Unit>> DeleteAsync(
        TransactionNumber transactionNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transactionNumber);

        return ExecuteAsync(
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "DELETE FROM PendingPayments WHERE TransactionNumber = $transactionNumber;";
                command.Parameters.AddWithValue("$transactionNumber", transactionNumber.Value);
                await command.ExecuteNonQueryAsync(token);

                return Unit.Value;
            },
            cancellationToken);
    }

    public Task<PortResult<IReadOnlyCollection<PaymentTransaction>>> LoadPendingAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            async (connection, token) =>
            {
                var transactions = new List<PaymentTransaction>();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT
                        TransactionNumber,
                        StoreId,
                        TerminalId,
                        CashierId,
                        CardValue,
                        CardKind,
                        Amount,
                        Currency,
                        Operation,
                        ReferenceNumber,
                        Status,
                        ReconciliationStatus
                    FROM PendingPayments
                    ORDER BY UpdatedAtUtc;
                    """;
                await using var reader = await command.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    transactions.Add(RestoreTransaction(reader));
                }

                return (IReadOnlyCollection<PaymentTransaction>)transactions;
            },
            cancellationToken);

    private async Task<PortResult<T>> ExecuteAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _ = SqlCipherInitialization.Value;
            EnsureDatabaseDirectoryExists();

            var keyResult = await _keyProvider.GetOrCreateAsync(cancellationToken);

            if (keyResult.IsFailure)
            {
                return PortResult<T>.Failed(keyResult.Failure);
            }

            using var key = keyResult.Value;
            await using var connection = new SqliteConnection(CreateConnectionString());
            await connection.OpenAsync(cancellationToken);
            await ConfigureEncryptedConnectionAsync(connection, key, cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);

            var result = await operation(connection, cancellationToken);
            return PortResult<T>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPersistenceException(exception))
        {
            return PortResult<T>.Failed(new PortFailure(
                "persistence.database_unavailable",
                "The encrypted pending-payment database is unavailable.",
                PortFailureType.Technical));
        }
    }

    private string CreateConnectionString() =>
        new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

    private static async Task ConfigureEncryptedConnectionAsync(
        SqliteConnection connection,
        DatabaseEncryptionKey key,
        CancellationToken cancellationToken)
    {
        var keyHex = Convert.ToHexString(key.Bytes.Span);
        await ExecutePragmaAsync(
            connection,
            $"PRAGMA key = \"x'{keyHex}'\";",
            cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA cipher_version;";
        var cipherVersion = await versionCommand.ExecuteScalarAsync(cancellationToken) as string;

        if (string.IsNullOrWhiteSpace(cipherVersion))
        {
            throw new InvalidDataException("SQLCipher is not available.");
        }

        await ExecutePragmaAsync(
            connection,
            "PRAGMA cipher_memory_security = ON;",
            cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken));

        if (version > SchemaVersion)
        {
            throw new InvalidDataException("The database schema is newer than this application.");
        }

        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS PendingPayments (
                TransactionNumber TEXT PRIMARY KEY NOT NULL,
                StoreId TEXT NOT NULL,
                TerminalId TEXT NOT NULL,
                CashierId TEXT NOT NULL,
                CardValue TEXT NOT NULL,
                CardKind INTEGER NOT NULL,
                Amount INTEGER NOT NULL,
                Currency TEXT NOT NULL,
                Operation INTEGER NOT NULL,
                ReferenceNumber TEXT NULL,
                Status INTEGER NOT NULL,
                ReconciliationStatus INTEGER NOT NULL,
                UpdatedAtUtc INTEGER NOT NULL
            );
            """;
        await schemaCommand.ExecuteNonQueryAsync(cancellationToken);

        if (version == 0)
        {
            await ExecutePragmaAsync(
                connection,
                $"PRAGMA user_version = {SchemaVersion};",
                cancellationToken);
        }
    }

    private static PaymentTransaction RestoreTransaction(SqliteDataReader reader)
    {
        var transactionNumber = TransactionNumber.Create(reader.GetString(0));
        var storeId = StoreId.Create(reader.GetString(1));
        var terminalId = TerminalId.Create(reader.GetString(2));
        var cashierId = CashierId.Create(reader.GetString(3));
        var cardKind = (CardIdentifierKind)reader.GetInt32(5);
        var cardIdentifier = cardKind switch
        {
            CardIdentifierKind.PhysicalCard => CardIdentifier.CreatePhysicalCard(reader.GetString(4)),
            CardIdentifierKind.DigitalGencode => CardIdentifier.CreateDigitalGencode(reader.GetString(4)),
            _ => throw new InvalidDataException("The card identifier kind is invalid.")
        };
        var amount = Money.Create(reader.GetInt64(6), reader.GetString(7));
        var referenceNumber = reader.IsDBNull(9)
            ? null
            : ReferenceNumber.Create(reader.GetString(9)).Value;

        if (transactionNumber.IsFailure ||
            storeId.IsFailure ||
            terminalId.IsFailure ||
            cashierId.IsFailure ||
            cardIdentifier.IsFailure ||
            amount.IsFailure)
        {
            throw new InvalidDataException("A persisted payment value is invalid.");
        }

        var state = new PaymentTransactionState(
            transactionNumber.Value,
            storeId.Value,
            terminalId.Value,
            cashierId.Value,
            cardIdentifier.Value,
            amount.Value,
            (GiftCardOperation)reader.GetInt32(8),
            referenceNumber,
            (PaymentStatus)reader.GetInt32(10),
            (ReconciliationStatus)reader.GetInt32(11));
        var transaction = PaymentTransaction.Restore(state);

        if (transaction.IsFailure)
        {
            throw new InvalidDataException("The persisted payment state is invalid.");
        }

        return transaction.Value;
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureDatabaseDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_databasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool IsPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            FormatException or
            TypeInitializationException or
            DllNotFoundException or
            InvalidOperationException;
}
