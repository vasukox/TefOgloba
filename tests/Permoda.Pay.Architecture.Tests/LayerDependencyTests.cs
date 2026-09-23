using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba;

namespace Permoda.Pay.Architecture.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceOuterLayers()
    {
        var references = GetReferences(typeof(PaymentTransaction).Assembly);

        Assert.DoesNotContain("Permoda.Pay.Application", references);
        Assert.DoesNotContain("Permoda.Pay.Infrastructure", references);
        Assert.DoesNotContain("Permoda.Pay.Maui", references);
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrMaui()
    {
        var references = GetReferences(typeof(ProcessSaleHandler).Assembly);

        Assert.DoesNotContain("Permoda.Pay.Infrastructure", references);
        Assert.DoesNotContain("Permoda.Pay.Maui", references);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceMaui()
    {
        var references = GetReferences(typeof(OglobaGiftCardProvider).Assembly);

        Assert.DoesNotContain("Permoda.Pay.Maui", references);
    }

    private static IReadOnlyCollection<string> GetReferences(System.Reflection.Assembly assembly) =>
        assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
}
