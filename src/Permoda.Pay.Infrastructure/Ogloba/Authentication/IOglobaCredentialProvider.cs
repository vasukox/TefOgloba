using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Infrastructure.Ogloba.Authentication;

public interface IOglobaCredentialProvider
{
    ValueTask<PortResult<OglobaStoreCredential>> GetAsync(
        StoreId storeId,
        CancellationToken cancellationToken);
}
