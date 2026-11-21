using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Licensing;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.Premium;

/// <summary>Row of the Free / Pro / Business comparison.</summary>
public sealed class PlanFeatureItem(FeatureInfo info, bool isHighlighted)
{
    public string Name => info.Name;
    public string Description => info.Description;
    public string Glyph => info.IconGlyph;
    public bool InFree => info.RequiredTier <= SubscriptionTier.Free;
    public bool InPro => info.RequiredTier <= SubscriptionTier.Pro;
    public bool InBusiness => info.RequiredTier <= SubscriptionTier.Business;
    public bool NotInFree => !InFree;
    public bool NotInPro => !InPro;
    public bool IsComingSoon => info.Availability == FeatureAvailability.ComingSoon;
    public bool IsHighlighted { get; } = isHighlighted;

    public string SemanticDescription =>
        $"{Name}. {(InFree ? "Free" : InPro ? "Pro and Business" : "Business only")}{(IsComingSoon ? ", coming soon" : string.Empty)}";
}

/// <summary>A purchasable product card.</summary>
public sealed class ProductCardItem(StoreProduct product, IAsyncRelayCommand<ProductCardItem?> purchaseCommand)
{
    public StoreProduct Product { get; } = product;
    public string ProductId => Product.ProductId;
    public string Title => Product.Title;
    public string Description => Product.Description;
    public string Price => Product.LocalizedPrice;
    public bool IsBestValue => Product.IsBestValue;
    public string TierName => $"QuickQr {Product.Tier}";
    public bool IsBusinessTier => Product.Tier == SubscriptionTier.Business;

    public string PeriodText => Product.Period switch
    {
        BillingPeriod.Monthly => "per month",
        BillingPeriod.Yearly => "per year",
        _ => "One-time purchase · yours forever"
    };

    public string ButtonText => Product.ProductId == ProductIds.BusinessUpgrade ? "Upgrade to Business" : $"Get {Product.Tier}";
    public string SemanticDescription => $"{Title}, {Price}, {PeriodText}{(IsBestValue ? ", best value" : string.Empty)}";
    public IAsyncRelayCommand<ProductCardItem?> PurchaseCommand { get; } = purchaseCommand;
}

public sealed partial class PaywallViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IEntitlementService _entitlements;
    private readonly PremiumGate _gate;
    private readonly INavigationService _navigation;
    private readonly IHapticsService _haptics;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private IReadOnlyList<StoreProduct> _allProducts = [];
    private bool _productsLoaded;
    private bool _closeWhenIdle;

    public PaywallViewModel(
        IEntitlementService entitlements,
        PremiumGate gate,
        INavigationService navigation,
        IHapticsService haptics,
        IDialogService dialogs,
        ILogger<PaywallViewModel> logger) : base(dialogs, logger)
    {
        _entitlements = entitlements;
        _gate = gate;
        _navigation = navigation;
        _haptics = haptics;
        Title = "QuickQr Pro";
        CurrentTier = entitlements.Tier;
        BuildFeatures();
    }

    public ObservableCollection<PlanFeatureItem> Features { get; } = [];
    public ObservableCollection<ProductCardItem> Products { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTierText), nameof(HasEverything), nameof(CanPurchase))]
    public partial SubscriptionTier CurrentTier { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHighlight), nameof(HeroTitle), nameof(HighlightName), nameof(HighlightDescription), nameof(HighlightGlyph), nameof(HighlightTierText))]
    public partial FeatureInfo? HighlightedFeature { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingProducts { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductsError))]
    public partial string? ProductsError { get; set; }

    [ObservableProperty]
    public partial bool HasProducts { get; set; }

    public bool HasHighlight => HighlightedFeature is not null;
    public string HeroTitle => HighlightedFeature is { } f ? $"Unlock {f.Name}" : "Unlock everything QuickQr can do";
    public string HighlightName => HighlightedFeature?.Name ?? string.Empty;
    public string HighlightDescription => HighlightedFeature?.Description ?? string.Empty;
    public string HighlightGlyph => HighlightedFeature?.IconGlyph ?? "\ue7af";
    public string HighlightTierText => HighlightedFeature is { } f ? $"Included in QuickQr {f.RequiredTier}" : string.Empty;
    public string CurrentTierText => $"You're on QuickQr {CurrentTier}";
    public bool HasEverything => CurrentTier == SubscriptionTier.Business;
    public bool CanPurchase => !HasEverything;
    public bool HasProductsError => !string.IsNullOrEmpty(ProductsError);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        HighlightedFeature = query.TryGetValue(Routes.FeatureParameter, out var value) &&
                             value is string name &&
                             Enum.TryParse<PremiumFeature>(name, ignoreCase: true, out var feature)
            ? FeatureCatalog.Get(feature)
            : null;
        BuildFeatures();
    }

    public override async Task OnAppearingAsync()
    {
        _gate.OnPaywallOpened();
        _closeWhenIdle = false;
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;
        _entitlements.EntitlementsChanged += OnEntitlementsChanged;
        CurrentTier = _entitlements.Tier;

        if (!_productsLoaded)
            await LoadProductsAsync();
        else
            RebuildProducts();
    }

    public override Task OnDisappearingAsync()
    {
        _entitlements.EntitlementsChanged -= OnEntitlementsChanged;

        // A store purchase sheet can hide the page mid-purchase; only release waiting callers once idle.
        if (IsBusy) _closeWhenIdle = true;
        else _gate.OnPaywallClosed();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CloseAsync() => _navigation.GoBackAsync();

    [RelayCommand]
    private async Task LoadProductsAsync()
    {
        IsLoadingProducts = true;
        ProductsError = null;
        try
        {
            _allProducts = await _entitlements.GetProductsAsync();
            _productsLoaded = true;
            RebuildProducts();
            if (_allProducts.Count == 0)
                ProductsError = "No products are available right now. Please try again later.";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading store products failed");
            ProductsError = "The store couldn't be reached. Check your connection and try again.";
        }
        finally
        {
            IsLoadingProducts = false;
        }
    }

    [RelayCommand]
    private async Task PurchaseAsync(ProductCardItem? item)
    {
        if (item is null) return;
        PurchaseOutcome? outcome = null;
        await RunBusyAsync(async () => outcome = await _entitlements.PurchaseAsync(item.ProductId), "Purchase failed");
        if (outcome is not null) await HandleOutcomeAsync(outcome, isRestore: false);
        ReleaseIfClosed();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        PurchaseOutcome? outcome = null;
        await RunBusyAsync(async () => outcome = await _entitlements.RestoreAsync(), "Restore failed");
        if (outcome is not null) await HandleOutcomeAsync(outcome, isRestore: true);
        ReleaseIfClosed();
    }

    private async Task HandleOutcomeAsync(PurchaseOutcome outcome, bool isRestore)
    {
        CurrentTier = _entitlements.Tier;

        if (outcome.IsSuccess)
        {
            _haptics.Success();
            await Dialogs.ToastAsync(isRestore
                ? $"Purchases restored: QuickQr {outcome.Tier}"
                : $"Welcome to QuickQr {outcome.Tier}! Everything is unlocked.");
            if (!_closeWhenIdle) await _navigation.GoBackAsync();
            return;
        }

        switch (outcome.Status)
        {
            case PurchaseStatus.Cancelled:
                return; // The user backed out: say nothing.
            case PurchaseStatus.Pending:
                await Dialogs.AlertAsync("Purchase pending",
                    "Your payment is being processed. Your new features unlock automatically as soon as it completes.");
                return;
            case PurchaseStatus.NotSupported:
                await Dialogs.AlertAsync("Purchases unavailable",
                    outcome.Message ?? "In-app purchases are not available on this device.");
                return;
            default:
                await Dialogs.AlertAsync(isRestore ? "Nothing to restore" : "Purchase failed",
                    outcome.Message ?? (isRestore
                        ? "No previous purchases were found for this store account."
                        : "The purchase could not be completed. Please try again later."));
                return;
        }
    }

    private void ReleaseIfClosed()
    {
        if (!_closeWhenIdle) return;
        _closeWhenIdle = false;
        _gate.OnPaywallClosed();
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        CurrentTier = _entitlements.Tier;
        RebuildProducts();
    });

    private void BuildFeatures()
    {
        Features.Clear();
        foreach (var info in FeatureCatalog.All)
            Features.Add(new PlanFeatureItem(info, info.Feature == HighlightedFeature?.Feature));
    }

    private void RebuildProducts()
    {
        Products.Clear();
        foreach (var product in _allProducts.Where(IsRelevant))
            Products.Add(new ProductCardItem(product, PurchaseCommand));
        HasProducts = Products.Count > 0;
    }

    /// <summary>Only offer what moves the user up: the discounted upgrade replaces full Business for Pro owners.</summary>
    private bool IsRelevant(StoreProduct product)
    {
        if (product.Tier <= CurrentTier) return false;
        if (product.ProductId == ProductIds.BusinessUpgrade) return CurrentTier == SubscriptionTier.Pro;
        if (product.Tier == SubscriptionTier.Business && CurrentTier == SubscriptionTier.Pro)
            return !_allProducts.Any(p => p.ProductId == ProductIds.BusinessUpgrade);
        return true;
    }

    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
}
