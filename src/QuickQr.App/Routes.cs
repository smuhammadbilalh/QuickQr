namespace QuickQr;

/// <summary>Shell routes. Tabs are absolute ("//create"); detail pages are pushed relative routes.</summary>
public static class Routes
{
    // Root tabs (AppShell.xaml)
    public const string Create = "create";
    public const string Scan = "scan";
    public const string History = "history";
    public const string Settings = "settings";

    // Pushed pages (registered by the feature registration extensions)
    public const string Paywall = "paywall";
    public const string Batch = "batch";
    public const string HistoryDetail = "historydetail";
    public const string ScanSession = "scansession";
    public const string About = "about";

    // Query parameter names
    public const string IdParameter = "id";
    public const string PayloadParameter = "payload";
    public const string FeatureParameter = "feature";
}
