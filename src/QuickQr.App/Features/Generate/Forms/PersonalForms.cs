using CommunityToolkit.Mvvm.ComponentModel;
using QuickQr.Controls;
using QuickQr.Core.Content;
using ContactPhone = QuickQr.Core.Content.ContactPhone;

namespace QuickQr.Features.Generate;

public sealed partial class WifiFormViewModel : ContentFormViewModel
{
    public WifiFormViewModel()
    {
        Security = new OptionGroup<WifiSecurity>()
            .Add(WifiSecurity.Wpa, "WPA/WPA2")
            .Add(WifiSecurity.Wpa3, "WPA3")
            .Add(WifiSecurity.Wep, "WEP")
            .Add(WifiSecurity.None, "Open");
        Security.SelectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Security));
            OnPropertyChanged(nameof(NeedsPassword));
        };
    }

    public override ContentKind Kind => ContentKind.Wifi;

    public OptionGroup<WifiSecurity> Security { get; }

    public bool NeedsPassword => Security.Value != WifiSecurity.None;

    [ObservableProperty]
    public partial string Ssid { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Hidden { get; set; }

    public override bool IsEmpty => string.IsNullOrEmpty(Ssid);

    public override QrContent BuildContent() =>
        new WifiContent(Ssid, NeedsPassword ? Password : null, Security.Value, Hidden);

    public override bool TryLoad(QrContent content)
    {
        if (content is not WifiContent wifi) return false;
        Ssid = wifi.Ssid;
        Password = wifi.Password ?? string.Empty;
        Security.Value = wifi.Security;
        Hidden = wifi.Hidden;
        return true;
    }
}

public sealed partial class ContactFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Contact;

    /// <summary>MeCard is shorter (smaller code) but holds fewer fields than vCard.</summary>
    [ObservableProperty]
    public partial bool UseMeCard { get; set; }

    [ObservableProperty]
    public partial string FirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Organization { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string JobTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MobilePhone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkPhone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Website { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Street { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string City { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Region { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PostalCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Country { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public override bool IsEmpty =>
        IsBlank(FirstName, LastName, Organization, MobilePhone, WorkPhone, Email, Website);

    public override QrContent BuildContent()
    {
        if (UseMeCard)
        {
            var name = string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            var address = string.Join(", ", new[] { Street, City, Region, PostalCode, Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            return new MeCardContent(
                name,
                Optional(MobilePhone) ?? Optional(WorkPhone),
                Optional(Email),
                Optional(Website),
                Optional(address),
                Optional(Note));
        }

        var phones = new List<ContactPhone>();
        if (Optional(MobilePhone) is { } mobile) phones.Add(new ContactPhone(mobile, PhoneKind.Mobile));
        if (Optional(WorkPhone) is { } work) phones.Add(new ContactPhone(work, PhoneKind.Work));

        PostalAddress? postal = IsBlank(Street, City, Region, PostalCode, Country)
            ? null
            : new PostalAddress(Optional(Street), Optional(City), Optional(Region), Optional(PostalCode), Optional(Country));

        return new ContactContent
        {
            FirstName = Optional(FirstName),
            LastName = Optional(LastName),
            Organization = Optional(Organization),
            JobTitle = Optional(JobTitle),
            Phones = phones,
            Emails = Optional(Email) is { } email ? new[] { email } : Array.Empty<string>(),
            Website = Optional(Website),
            Address = postal,
            Note = Optional(Note),
        };
    }

    public override bool TryLoad(QrContent content)
    {
        switch (content)
        {
            case ContactContent contact:
                UseMeCard = false;
                FirstName = contact.FirstName ?? string.Empty;
                LastName = contact.LastName ?? string.Empty;
                Organization = contact.Organization ?? string.Empty;
                JobTitle = contact.JobTitle ?? string.Empty;
                MobilePhone = contact.Phones.FirstOrDefault(p => p.Kind != PhoneKind.Work)?.Number ?? string.Empty;
                WorkPhone = contact.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Work)?.Number ?? string.Empty;
                Email = contact.Emails.FirstOrDefault() ?? string.Empty;
                Website = contact.Website ?? string.Empty;
                Street = contact.Address?.Street ?? string.Empty;
                City = contact.Address?.City ?? string.Empty;
                Region = contact.Address?.Region ?? string.Empty;
                PostalCode = contact.Address?.PostalCode ?? string.Empty;
                Country = contact.Address?.Country ?? string.Empty;
                Note = contact.Note ?? string.Empty;
                return true;

            case MeCardContent meCard:
                UseMeCard = true;
                FirstName = meCard.Name;
                LastName = string.Empty;
                MobilePhone = meCard.Phone ?? string.Empty;
                Email = meCard.Email ?? string.Empty;
                Website = meCard.Url ?? string.Empty;
                Street = meCard.Address ?? string.Empty;
                Note = meCard.Note ?? string.Empty;
                return true;

            default:
                return false;
        }
    }
}

public sealed partial class GeoFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Location;

    [ObservableProperty]
    public partial string Latitude { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Longitude { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    public override bool IsEmpty => IsBlank(Latitude, Longitude);

    public override string? Validate()
    {
        if (!TryParseDouble(Latitude, out var lat) || lat is < -90 or > 90)
            return "Enter a latitude between -90 and 90, for example 51.5074.";
        if (!TryParseDouble(Longitude, out var lng) || lng is < -180 or > 180)
            return "Enter a longitude between -180 and 180, for example -0.1278.";
        return null;
    }

    public override QrContent BuildContent()
    {
        TryParseDouble(Latitude, out var lat);
        TryParseDouble(Longitude, out var lng);
        return new GeoContent(lat, lng, Query: Optional(Label));
    }

    public override bool TryLoad(QrContent content)
    {
        if (content is not GeoContent geo) return false;
        Latitude = Format(geo.Latitude);
        Longitude = Format(geo.Longitude);
        Label = geo.Query ?? string.Empty;
        return true;
    }
}

public sealed partial class EventFormViewModel : ContentFormViewModel
{
    public EventFormViewModel()
    {
        var start = DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);
        StartDate = start.Date;
        StartTime = start.TimeOfDay;
        var end = start.AddHours(1);
        EndDate = end.Date;
        EndTime = end.TimeOfDay;
    }

    public override ContentKind Kind => ContentKind.Event;

    [ObservableProperty]
    public partial string EventTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTimes))]
    public partial bool AllDay { get; set; }

    [ObservableProperty]
    public partial DateTime? StartDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? StartTime { get; set; }

    [ObservableProperty]
    public partial DateTime? EndDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EndTime { get; set; }

    [ObservableProperty]
    public partial string Location { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    public bool ShowTimes => !AllDay;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(EventTitle);

    public override string? Validate()
    {
        if (StartDate is null || EndDate is null) return "Choose a start and end date.";
        return End < Start ? "The event must end after it starts." : null;
    }

    private DateTimeOffset Start => Combine(StartDate, AllDay ? TimeSpan.Zero : StartTime);

    private DateTimeOffset End => AllDay
        ? Combine(EndDate, TimeSpan.Zero)
        : Combine(EndDate, EndTime);

    public override QrContent BuildContent() =>
        new CalendarEventContent(Required(EventTitle), Start, End, AllDay, Optional(Location), Optional(Description));

    public override bool TryLoad(QrContent content)
    {
        if (content is not CalendarEventContent calendar) return false;
        EventTitle = calendar.Title;
        AllDay = calendar.AllDay;
        var start = calendar.Start.ToLocalTime();
        var end = calendar.End.ToLocalTime();
        StartDate = start.Date;
        StartTime = start.TimeOfDay;
        EndDate = end.Date;
        EndTime = end.TimeOfDay;
        Location = calendar.Location ?? string.Empty;
        Description = calendar.Description ?? string.Empty;
        return true;
    }

    private static DateTimeOffset Combine(DateTime? date, TimeSpan? time)
    {
        var local = DateTime.SpecifyKind((date ?? DateTime.Today).Date + (time ?? TimeSpan.Zero), DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}
