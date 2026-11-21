using QuickQr.Core.Security;

namespace QuickQr.Features.Generate;

/// <summary>Creates the form view model for each picker entry.</summary>
public static class ContentFormFactory
{
    public static ContentFormViewModel Create(ContentKind kind, ISecurePayloadProtector protector) => kind switch
    {
        ContentKind.Text => new TextFormViewModel(),
        ContentKind.Url => new UrlFormViewModel(),
        ContentKind.Wifi => new WifiFormViewModel(),
        ContentKind.Contact => new ContactFormViewModel(),
        ContentKind.Email => new EmailFormViewModel(),
        ContentKind.Sms => new SmsFormViewModel(),
        ContentKind.Phone => new PhoneFormViewModel(),
        ContentKind.Location => new GeoFormViewModel(),
        ContentKind.Event => new EventFormViewModel(),
        ContentKind.WhatsApp => new WhatsAppFormViewModel(),
        ContentKind.Telegram => new TelegramFormViewModel(),
        ContentKind.Crypto => new CryptoFormViewModel(),
        ContentKind.Sepa => new SepaFormViewModel(),
        ContentKind.Upi => new UpiFormViewModel(),
        ContentKind.AppStore => new AppStoreFormViewModel(),
        ContentKind.Social => new SocialFormViewModel(),
        ContentKind.Product => new ProductFormViewModel(),
        ContentKind.Secure => new SecureMessageFormViewModel(protector),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

/// <summary>Picks the form layout (from <see cref="ContentFormTemplates"/>) for a <see cref="ContentFormViewModel"/>.</summary>
public sealed class ContentFormTemplateSelector : DataTemplateSelector
{
    private readonly ContentFormTemplates _templates = new();

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        var key = item switch
        {
            TextFormViewModel => "TextForm",
            UrlFormViewModel => "UrlForm",
            WifiFormViewModel => "WifiForm",
            ContactFormViewModel => "ContactForm",
            EmailFormViewModel => "EmailForm",
            SmsFormViewModel => "SmsForm",
            PhoneFormViewModel => "PhoneForm",
            GeoFormViewModel => "GeoForm",
            EventFormViewModel => "EventForm",
            WhatsAppFormViewModel => "WhatsAppForm",
            TelegramFormViewModel => "TelegramForm",
            CryptoFormViewModel => "CryptoForm",
            SepaFormViewModel => "SepaForm",
            UpiFormViewModel => "UpiForm",
            AppStoreFormViewModel => "AppStoreForm",
            SocialFormViewModel => "SocialForm",
            ProductFormViewModel => "ProductForm",
            SecureMessageFormViewModel => "SecureForm",
            _ => throw new ArgumentException($"No form template for {item.GetType().Name}.", nameof(item))
        };
        return (DataTemplate)_templates[key];
    }
}
