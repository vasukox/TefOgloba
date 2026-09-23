using System.Text;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Persistence;
using Permoda.Pay.Infrastructure.Tests.TestDoubles;

namespace Permoda.Pay.Infrastructure.Tests.Persistence;

public sealed class SqlCipherPendingPaymentRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "TefOgloba.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_PreservesRecoverableTransactionState()
    {
        var repository = CreateRepository(CreateKey(1));
        var transaction = CreateRequestedTransaction();
        transaction.PrepareConfirmation();

        var saveResult = await repository.SaveAsync(transaction, CancellationToken.None);
        var loadResult = await repository.LoadPendingAsync(CancellationToken.None);

        Assert.True(saveResult.IsSuccess);
        Assert.True(loadResult.IsSuccess);

        var loaded = Assert.Single(loadResult.Value);
        Assert.Equal(transaction.TransactionNumber, loaded.TransactionNumber);
        Assert.Equal(transaction.ReferenceNumber, loaded.ReferenceNumber);
        // PAN enmascarado en disco: al guardar guardamos el MaskedValue, no el Value crudo.
        // Lo que vuelve del repo es un CardIdentifier cuyo Value es el PAN enmascarado y
        // cuyo MaskedValue es el mismo (la máscara de una máscara es estable).
        Assert.Equal(transaction.CardIdentifier.MaskedValue, loaded.CardIdentifier.MaskedValue);
        Assert.Equal(PaymentStatus.ConfirmationPending, loaded.Status);
        Assert.Equal(ReconciliationStatus.NotRequired, loaded.ReconciliationStatus);
    }

    [Fact]
    public async Task DatabaseFiles_DoNotContainPlaintextTransactionData()
    {
        var repository = CreateRepository(CreateKey(2));
        var transaction = CreateRequestedTransaction();

        var saveResult = await repository.SaveAsync(transaction, CancellationToken.None);

        Assert.True(saveResult.IsSuccess);

        var files = Directory.GetFiles(_directory);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var content = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain("1138170025515937", content, StringComparison.Ordinal);
            Assert.DoesNotContain("00136544716V", content, StringComparison.Ordinal);
            Assert.DoesNotContain("K00036", content, StringComparison.Ordinal);
        }

        var databaseHeader = new byte[16];
        await using var stream = File.OpenRead(GetDatabasePath());
        var bytesRead = await stream.ReadAsync(databaseHeader);

        Assert.Equal(databaseHeader.Length, bytesRead);
        Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(databaseHeader));
    }

    [Fact]
    public async Task Load_WithDifferentEncryptionKey_ReturnsTechnicalFailure()
    {
        var repository = CreateRepository(CreateKey(3));
        var transaction = CreateRequestedTransaction();
        var saveResult = await repository.SaveAsync(transaction, CancellationToken.None);
        Assert.True(saveResult.IsSuccess);

        var repositoryWithWrongKey = CreateRepository(CreateKey(4));
        var loadResult = await repositoryWithWrongKey.LoadPendingAsync(CancellationToken.None);

        Assert.True(loadResult.IsFailure);
        Assert.Equal("persistence.database_unavailable", loadResult.Failure.Code);
    }

    [Fact]
    public async Task Save_UpsertsStateAndDeleteRemovesPendingTransaction()
    {
        var repository = CreateRepository(CreateKey(5));
        var transaction = CreateRequestedTransaction();
        var firstSave = await repository.SaveAsync(transaction, CancellationToken.None);
        Assert.True(firstSave.IsSuccess);

        transaction.PrepareConfirmation();
        var secondSave = await repository.SaveAsync(transaction, CancellationToken.None);
        var loadResult = await repository.LoadPendingAsync(CancellationToken.None);

        Assert.True(secondSave.IsSuccess);
        Assert.Equal(
            PaymentStatus.ConfirmationPending,
            Assert.Single(loadResult.Value).Status);

        var deleteResult = await repository.DeleteAsync(
            transaction.TransactionNumber,
            CancellationToken.None);
        var emptyResult = await repository.LoadPendingAsync(CancellationToken.None);

        Assert.True(deleteResult.IsSuccess);
        Assert.Empty(emptyResult.Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private SqlCipherPendingPaymentRepository CreateRepository(byte[] key) =>
        new(
            GetDatabasePath(),
            new StubDatabaseEncryptionKeyProvider(key));

    private string GetDatabasePath() => Path.Combine(_directory, "pending-payments.db");

    private static PaymentTransaction CreateRequestedTransaction()
    {
        var transaction = PaymentTransaction.Create(
            TransactionNumber.Create("1756113296").Value,
            StoreId.Create("K00036").Value,
            TerminalId.Create("caja-5").Value,
            CashierId.Create("operador-123").Value,
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value,
            GiftCardOperation.Redemption).Value;
        transaction.RegisterRequest(ReferenceNumber.Create("00136544716V").Value);
        return transaction;
    }

    private static byte[] CreateKey(byte seed) =>
        Enumerable.Range(0, 32).Select(index => (byte)(seed + index)).ToArray();
}
