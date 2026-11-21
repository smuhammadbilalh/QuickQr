using CommunityToolkit.Mvvm.ComponentModel;
using QuickQr.Core.Content;

namespace QuickQr.Features.Generate;

public sealed partial class TextFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Text;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrEmpty(Text);

    public override QrContent BuildContent() => new TextContent(Text);

    public override bool TryLoad(QrContent content)
    {
        if (content is not TextContent text) return false;
        Text = text.Text;
        return true;
    }
}

public sealed partial class UrlFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Url;

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Url);

    /// <summary>Adds https:// when the user typed a bare host such as "example.com".</summary>
    public override QrContent BuildContent()
    {
        var url = Required(Url);
        if (url.Length > 0 && !url.Contains("://", StringComparison.Ordinal) &&
            !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        return new UrlContent(url);
    }

    public override bool TryLoad(QrContent content)
    {
        if (content is not UrlContent url) return false;
        Url = url.Url;
        return true;
    }
}

public sealed partial class PhoneFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Phone;

    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Number);

    public override QrContent BuildContent() => new PhoneContent(Required(Number));

    public override bool TryLoad(QrContent content)
    {
        if (content is not PhoneContent phone) return false;
        Number = phone.Number;
        return true;
    }
}

public sealed partial class SmsFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Sms;

    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(Number, Message);

    public override QrContent BuildContent() => new SmsContent(Required(Number), Optional(Message));

    public override bool TryLoad(QrContent content)
    {
        if (content is not SmsContent sms) return false;
        Number = sms.Number;
        Message = sms.Message ?? string.Empty;
        return true;
    }
}

public sealed partial class EmailFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Email;

    [ObservableProperty]
    public partial string To { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Cc { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Bcc { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(To, Cc, Bcc, Subject, Body);

    public override QrContent BuildContent() =>
        new EmailContent(Required(To), Optional(Subject), Optional(Body), Optional(Cc), Optional(Bcc));

    public override bool TryLoad(QrContent content)
    {
        if (content is not EmailContent email) return false;
        To = email.To;
        Cc = email.Cc ?? string.Empty;
        Bcc = email.Bcc ?? string.Empty;
        Subject = email.Subject ?? string.Empty;
        Body = email.Body ?? string.Empty;
        return true;
    }
}

public sealed partial class WhatsAppFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.WhatsApp;

    [ObservableProperty]
    public partial string Number { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(Number, Message);

    public override QrContent BuildContent() => new WhatsAppContent(Required(Number), Optional(Message));

    public override bool TryLoad(QrContent content)
    {
        if (content is not WhatsAppContent whatsApp) return false;
        Number = whatsApp.Number;
        Message = whatsApp.Message ?? string.Empty;
        return true;
    }
}

public sealed partial class TelegramFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Telegram;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Username);

    public override QrContent BuildContent() => new TelegramContent(Required(Username).TrimStart('@'));

    public override bool TryLoad(QrContent content)
    {
        if (content is not TelegramContent telegram) return false;
        Username = telegram.Username;
        return true;
    }
}
