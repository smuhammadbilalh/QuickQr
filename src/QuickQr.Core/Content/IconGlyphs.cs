namespace QuickQr.Core.Content;

/// <summary>
/// Material Icons codepoints (MaterialIcons-Regular.ttf) used for content types and actions.
/// Set the font family to Material Icons and use these strings as the label text.
/// </summary>
public static class IconGlyphs
{
    // ---- Glyphs by Material icon name ----

    public const string Language = "\uea07";
    public const string Link = "\ue250";
    public const string Wifi = "\ue63e";
    public const string Person = "\uf0d3";
    public const string PersonAdd = "\uea4d";
    public const string AccountCircle = "\uf20b";
    public const string Email = "\ue159";
    public const string Sms = "\ue625";
    public const string Phone = "\uf0d4";
    public const string Place = "\uf1db";
    public const string Map = "\ue55b";
    public const string Directions = "\ue52e";
    public const string Event = "\ue878";
    public const string EventAvailable = "\ue614";
    public const string Chat = "\ue0c9";
    public const string Send = "\ue163";
    public const string AccountBalanceWallet = "\ue850";
    public const string AccountBalance = "\ue84f";
    public const string Payment = "\ue8a1";
    public const string Lock = "\ue899";
    public const string LockOpen = "\ue898";
    public const string Shop = "\ue8c9";
    public const string ShoppingCart = "\ue8cc";
    public const string Share = "\ue80d";
    public const string Copy = "\ue14d";
    public const string OpenInNew = "\ue89e";
    public const string Search = "\uef7a";
    public const string TextFields = "\ue262";
    public const string QrCode = "\ue00a";
    public const string VpnKey = "\ue0da";
    public const string Security = "\ue32a";
    public const string Warning = "\uf083";
    public const string Error = "\uf8b6";
    public const string Info = "\ue88e";
    public const string CheckCircle = "\uf0be";

    // ---- Semantic aliases (content types) ----

    public const string Text = TextFields;
    public const string Url = Language;
    public const string Contact = Person;
    public const string Calendar = Event;
    public const string WhatsApp = Chat;
    public const string Telegram = Send;
    public const string Wallet = AccountBalanceWallet;
    public const string Bank = AccountBalance;
    public const string SecureMessage = Lock;
    public const string AppStore = Shop;
    public const string SocialProfile = AccountCircle;
    public const string Product = ShoppingCart;
    public const string Location = Place;

    // ---- Semantic aliases (actions) ----

    public const string Open = OpenInNew;
    public const string Password = VpnKey;
    public const string AddContact = PersonAdd;
    public const string AddToCalendar = EventAvailable;
    public const string Navigate = Directions;
    public const string Decrypt = LockOpen;
    public const string Call = Phone;

    /// <summary>The icon representing a content type.</summary>
    public static string ForContentType(ContentType type) => type switch
    {
        ContentType.Text => Text,
        ContentType.Url => Url,
        ContentType.Wifi => Wifi,
        ContentType.Contact or ContentType.MeCard => Contact,
        ContentType.Email => Email,
        ContentType.Sms => Sms,
        ContentType.Phone => Phone,
        ContentType.Geo => Location,
        ContentType.CalendarEvent => Calendar,
        ContentType.WhatsApp => WhatsApp,
        ContentType.Telegram => Telegram,
        ContentType.CryptoPayment => Wallet,
        ContentType.EpcPayment => Bank,
        ContentType.UpiPayment => Wallet,
        ContentType.SecureMessage => SecureMessage,
        ContentType.AppStore => AppStore,
        ContentType.SocialProfile => SocialProfile,
        ContentType.Product => Product,
        _ => QrCode,
    };
}
