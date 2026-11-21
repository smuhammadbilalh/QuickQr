using Microsoft.Extensions.Time.Testing;
using QuickQr.Core.Licensing;
using QuickQr.Core.Platform;
using QuickQr.Infrastructure.Licensing;
using QuickQr.Infrastructure.Tests.Fakes;

namespace QuickQr.Infrastructure.Tests.Licensing;

public sealed class SimulatedPurchaseProviderTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryKeyValueStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Offers_exactly_the_one_time_products()
    {
        var products = await Create().GetProductsAsync(ProductIds.All, Ct);

        products.Select(p => (p.ProductId, p.LocalizedPrice, p.Period)).Should().Equal(
            (ProductIds.ProLifetime, "$9.99", BillingPeriod.Lifetime),
            (ProductIds.BusinessLifetime, "$29.99", BillingPeriod.Lifetime),
            (ProductIds.BusinessUpgrade, "$19.99", BillingPeriod.Lifetime));
        products.Single(p => p.IsBestValue).ProductId.Should().Be(ProductIds.ProLifetime);
        products.Single(p => p.ProductId == ProductIds.BusinessUpgrade).Title.Should().Contain("Upgrade Pro to Business");
        products.Select(p => p.Tier).Should().Equal(SubscriptionTier.Pro, SubscriptionTier.Business, SubscriptionTier.Business);
        (await Create().GetProductsAsync([ProductIds.BusinessLifetime], Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Purchases_are_persisted_and_survive_restarts()
    {
        var (status, purchase, _) = await Create().PurchaseAsync(ProductIds.ProLifetime, Ct);

        status.Should().Be(PurchaseStatus.Purchased);
        purchase!.ExpiresAt.Should().BeNull();
        purchase.PurchasedAt.Should().Be(_clock.GetUtcNow());
        _store.Get("simstore.purchases").Should().Contain(ProductIds.ProLifetime);

        var owned = await Create().GetOwnedPurchasesAsync(Ct);
        owned.Should().ContainSingle().Which.Should().Be(purchase);
    }

    [Fact]
    public async Task Buying_twice_reports_already_owned()
    {
        var provider = Create();
        await provider.PurchaseAsync(ProductIds.BusinessLifetime, Ct);

        var (status, purchase, message) = await provider.PurchaseAsync(ProductIds.BusinessLifetime, Ct);

        status.Should().Be(PurchaseStatus.AlreadyOwned);
        purchase.Should().NotBeNull();
        message.Should().NotBeNullOrEmpty();
        (await provider.GetOwnedPurchasesAsync(Ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Unknown_products_fail_and_corrupt_storage_is_ignored()
    {
        _store.Set("simstore.purchases", "[{oops");
        var provider = Create();

        (await provider.GetOwnedPurchasesAsync(Ct)).Should().BeEmpty();
        (await provider.PurchaseAsync(ProductIds.ProMonthly, Ct)).Status.Should().Be(PurchaseStatus.Failed, "subscriptions are not sold");
        (await provider.PurchaseAsync("nope", Ct)).Status.Should().Be(PurchaseStatus.Failed);
    }

    [Fact]
    public async Task Expired_records_are_not_reported_as_owned()
    {
        _store.Set("simstore.purchases",
            """[{"productId":"quickqr.pro.monthly","purchaseToken":"t","purchasedAt":"2026-01-01T00:00:00+00:00","expiresAt":"2026-02-01T00:00:00+00:00"}]""");

        (await Create().GetOwnedPurchasesAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_forgets_purchases()
    {
        var provider = Create();
        await provider.PurchaseAsync(ProductIds.ProLifetime, Ct);

        provider.Reset();

        (await provider.GetOwnedPurchasesAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Simulates_latency_on_the_time_provider()
    {
        var provider = new SimulatedPurchaseProvider(_store, _clock);
        provider.Latency.Should().Be(TimeSpan.FromMilliseconds(600));

        var pending = provider.GetOwnedPurchasesAsync(Ct);
        pending.IsCompleted.Should().BeFalse();

        _clock.Advance(TimeSpan.FromMilliseconds(600));
        (await pending).Should().BeEmpty();
    }

    [Fact]
    public async Task Drives_the_entitlement_service_end_to_end()
    {
        var entitlements = new EntitlementService(Create(), new InMemorySecureKeyValueStore(), _clock);
        await entitlements.InitializeAsync(Ct);

        (await entitlements.PurchaseAsync(ProductIds.ProLifetime, Ct)).Status.Should().Be(PurchaseStatus.Purchased);
        (await entitlements.PurchaseAsync(ProductIds.BusinessUpgrade, Ct)).Tier.Should().Be(SubscriptionTier.Business);
        (await entitlements.RestoreAsync(Ct)).Should().Be(new PurchaseOutcome(PurchaseStatus.Restored, SubscriptionTier.Business));
    }

    private SimulatedPurchaseProvider Create() => new(_store, _clock) { Latency = TimeSpan.Zero };
}
