using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba.Authentication;

namespace Permoda.Pay.Infrastructure.Tests.TestDoubles;

internal sealed class StubOglobaCredentialProvider : IOglobaCredentialProvider
{
    private readonly PortResult<OglobaStoreCredential> _result;

    public StubOglobaCredentialProvider(string password)
    {
        _result = PortResult<OglobaStoreCredential>.Success(
            new OglobaStoreCredential(password));
    }

    public StubOglobaCredentialProvider(PortFailure failure)
    {
        _result = PortResult<OglobaStoreCredential>.Failed(failure);
    }

    public List<string> RequestedStoreIds { get; } = [];

    public ValueTask<PortResult<OglobaStoreCredential>> GetAsync(
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedStoreIds.Add(storeId.Value);
        return ValueTask.FromResult(_result);
    }
}
