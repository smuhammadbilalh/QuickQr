using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using SkiaSharp;
using System.ComponentModel;
using System.IO;
using ZXing;
using ZXing.Common;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
namespace QuickQr;

public partial class ScanPage : ContentPage, INotifyPropertyChanged
{
    private string _scannedResult = "";
    private bool _hasScannedResult = false;
    private bool _isScanning = false;
    private Frame _dynamicCameraFrame;
    private ZXing.Net.Maui.Controls.CameraBarcodeReaderView _dynamicCameraView;

    public string ScannedResult
    {
        get => _scannedResult;
        set
        {
            _scannedResult = value;
            OnPropertyChanged();
            HasScannedResult = !string.IsNullOrEmpty(value);
        }
    }

    public bool HasScannedResult
    {
        get => _hasScannedResult;
        set
        {
            _hasScannedResult = value;
            OnPropertyChanged();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            _isScanning = value;
            OnPropertyChanged();
        }
    }

    public ScanPage()
    {
        InitializeComponent();
        BindingContext = this;
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RequestCameraPermissions();

        // Reset UI state
        IsScanning = false;
        RemoveDynamicCameraView(); // Clean up any existing camera view

        // Show progress ring for 1 second
        ProgressOverlay.IsVisible = true;
        PageLoadingIndicator.IsRunning = true;

        await Task.Delay(400);

        // Hide the progress ring with a smooth fade out
        await ProgressOverlay.FadeTo(0, 300);
        ProgressOverlay.IsVisible = false;
        PageLoadingIndicator.IsRunning = false;

        // Reset opacity for next time
        ProgressOverlay.Opacity = 1;
    }

    private async Task RequestCameraPermissions()
    {
        try
        {
            var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
            if (cameraStatus != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Required", "Camera permission is required for QR scanning", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Permission Error", $"Failed to request permissions: {ex.Message}", "OK");
        }
    }

    private async void OnStartScanClicked(object sender, EventArgs e)
    {
        try
        {
            // Check and request camera permission
            var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (cameraStatus != PermissionStatus.Granted)
            {
                cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                if (cameraStatus != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permission Denied", "Camera permission is required for scanning", "OK");
                    return;
                }
            }

            // Ensure we're on the main thread
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Create dynamic camera view
                    CreateDynamicCameraView();

                    // Hide scanner instructions
                    ScannerInstructions.IsVisible = false;

                    // Small delay before starting detection
                    await Task.Delay(200);

                    // Start detection
                    _dynamicCameraView.IsDetecting = true;
                    IsScanning = true;
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Camera Error", $"Failed to initialize camera: {ex.Message}", "OK");
                }
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to start camera: {ex.Message}", "OK");
        }
    }

    private async void OnStopScanClicked(object sender, EventArgs e)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_dynamicCameraView != null)
                {
                    _dynamicCameraView.IsDetecting = false;
                }

                IsScanning = false;

                // Remove dynamic camera view
                RemoveDynamicCameraView();

                // Show scanner instructions again
                ScannerInstructions.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to stop camera: {ex.Message}", "OK");
        }
    }
    private async void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        try
        {
            if (e.Results?.Any() == true && IsScanning)
            {
                var result = e.Results.First();

                // Stop scanning immediately to prevent multiple detections
                IsScanning = false;

                // Update UI on main thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        // Stop camera detection
                        if (_dynamicCameraView != null)
                        {
                            _dynamicCameraView.IsDetecting = false;
                        }

                        // Remove dynamic camera view
                        RemoveDynamicCameraView();

                        // Show scanner instructions again
                        ScannerInstructions.IsVisible = true;

                        // Update result
                        ScannedResult = result.Value;

                        // Process the scanned content automatically
                        await ProcessScannedContent(result.Value);
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Error", $"Failed to process scan result: {ex.Message}", "OK");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Scan Error", $"Error during barcode detection: {ex.Message}", "OK");
            });
        }
    }

    private void CreateDynamicCameraView()
    {
        // Remove any existing camera view first
        RemoveDynamicCameraView();

        // Create the camera view
        _dynamicCameraView = new ZXing.Net.Maui.Controls.CameraBarcodeReaderView
        {
            IsVisible = true,
            IsDetecting = false,
            HeightRequest = 300,
            CameraLocation = ZXing.Net.Maui.CameraLocation.Rear
        };

        // Subscribe to barcode detection event
        _dynamicCameraView.BarcodesDetected += OnBarcodesDetected;

        // Create stop button
        var stopButton = new Button
        {
            Text = "Stop Scanning",
            Margin = new Thickness(0, 10)
        };

        // Apply the AccentButton style

        stopButton.TextColor = Colors.White;
        stopButton.FontAttributes = FontAttributes.Bold;
        stopButton.TextTransform = TextTransform.Uppercase;
        stopButton.CornerRadius = 12;
        stopButton.HeightRequest = 52;
        stopButton.FontSize = 14;

        stopButton.Clicked += OnStopScanClicked;

        // Create separator with direct color assignment
        var separatorBox = new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = Application.Current.RequestedTheme == AppTheme.Light ?
                Color.FromArgb("#E2E8F0") : Color.FromArgb("#334155")
        };

        // Create container for camera view content
        var cameraContent = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { _dynamicCameraView, separatorBox, stopButton }
        };

        // Create frame for camera view
        _dynamicCameraFrame = new Frame
        {
            CornerRadius = 16,
            Padding = 0,
            Content = cameraContent
        };



        // Insert camera frame into the scanner container
        // Find the position after the Grid (scanner options)
        var insertIndex = 1; // After the Grid with buttons
        ScannerContainer.Insert(insertIndex, _dynamicCameraFrame);
    }





    // New method to remove dynamic camera view
    private void RemoveDynamicCameraView()
    {
        if (_dynamicCameraFrame != null && ScannerContainer.Children.Contains(_dynamicCameraFrame))
        {
            // Unsubscribe from events
            if (_dynamicCameraView != null)
            {
                _dynamicCameraView.BarcodesDetected -= OnBarcodesDetected;
                _dynamicCameraView.IsDetecting = false;
            }

            // Remove from container
            ScannerContainer.Remove(_dynamicCameraFrame);

            // Clean up references
            _dynamicCameraFrame = null;
            _dynamicCameraView = null;
        }
    }


    private async void OnCopyResultClicked(object sender, EventArgs e)
    {
        // Assuming ScannedResult is a property in your ViewModel, or use ScannedResultLabel.Text if not using MVVM
        var textToCopy = ScannedResultLabel?.Text;
        if (!string.IsNullOrEmpty(textToCopy))
        {
            await Clipboard.Default.SetTextAsync(textToCopy);
            // Optional: Provide user feedback
            await DisplayAlert("Copied", "Scan result copied to clipboard.", "OK");
        }
    }
    private async void OnScanFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select QR Code Image",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                using var stream = await result.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // Decode using SkiaSharp and ZXing.Net
                var bitmap = SKBitmap.Decode(memoryStream.ToArray());

                if (bitmap != null)
                {
                    // Convert SKBitmap to ZXing format
                    var width = bitmap.Width;
                    var height = bitmap.Height;
                    var luminances = new byte[width * height];

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            // Convert to grayscale using standard formula
                            luminances[y * width + x] = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                        }
                    }

                    var luminanceSource = new RGBLuminanceSource(luminances, width, height);
                    var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminanceSource));

                    var reader = new ZXing.QrCode.QRCodeReader();
                    var decodeResult = reader.decode(binaryBitmap);

                    if (decodeResult != null)
                    {
                        ScannedResult = decodeResult.Text;
                        await ProcessScannedContent(decodeResult.Text);
                    }
                    else
                    {
                        // Try with MultiFormatReader for better compatibility
                        var multiReader = new MultiFormatReader();
                        var genericResult = multiReader.decode(binaryBitmap);

                        if (genericResult != null)
                        {
                            ScannedResult = genericResult.Text;
                            await ProcessScannedContent(genericResult.Text);
                        }
                        else
                        {
                            await DisplayAlert("Error", "No QR code found in the selected image", "OK");
                        }
                    }
                }
                else
                {
                    await DisplayAlert("Error", "Failed to load the selected image", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to scan file: {ex.Message}", "OK");
        }
    }

    private async Task ProcessScannedContent(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                await DisplayAlert("QR Code Scanned", "Empty content detected", "OK");
                return;
            }

            var contentLower = content.ToLower().Trim();

            // Detect and handle different QR code types
            if (IsUrl(content))
            {
                await HandleUrl(content);
            }
            else if (IsWifiQR(content))
            {
                await HandleWifi(content);
            }
            else if (IsPhoneNumber(content))
            {
                await HandlePhoneNumber(content);
            }
            else if (IsEmail(content))
            {
                await HandleEmail(content);
            }
            else if (IsSms(content))
            {
                await HandleSms(content);
            }
            else if (IsGeoLocation(content))
            {
                await HandleGeoLocation(content);
            }
            else if (IsVCard(content))
            {
                await HandleVCard(content);
            }
            else
            {
                // Plain text - show with copy option
                await HandlePlainText(content);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Processing Error", $"Failed to process QR content: {ex.Message}", "OK");
        }
    }

    private bool IsUrl(string content)
    {
        return Uri.TryCreate(content, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == "ftp" || uri.Scheme == "ftps");
    }

    private bool IsWifiQR(string content)
    {
        return content.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPhoneNumber(string content)
    {
        var phonePattern = @"^(\+\d{1,3}[- ]?)?\d{10}$|^tel:";
        return System.Text.RegularExpressions.Regex.IsMatch(content, phonePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool IsEmail(string content)
    {
        return content.Contains("@") && (content.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
               System.Text.RegularExpressions.Regex.IsMatch(content, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"));
    }

    private bool IsSms(string content)
    {
        return content.StartsWith("sms:", StringComparison.OrdinalIgnoreCase) ||
               content.StartsWith("smsto:", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsGeoLocation(string content)
    {
        return content.StartsWith("geo:", StringComparison.OrdinalIgnoreCase) ||
               System.Text.RegularExpressions.Regex.IsMatch(content, @"^-?\d+\.?\d*,-?\d+\.?\d*$");
    }

    private bool IsVCard(string content)
    {
        return content.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleUrl(string url)
    {
        var action = await DisplayActionSheet($"URL Detected: {url}", "Cancel", null, "Open in Browser", "Copy URL");

        switch (action)
        {
            case "Open in Browser":
                try
                {
                    await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to open URL: {ex.Message}", "OK");
                }
                break;
            case "Copy URL":
                await Clipboard.Default.SetTextAsync(url);
                await DisplayAlert("Success", "URL copied to clipboard", "OK");
                break;
        }
    }

    private async Task HandleWifi(string wifiContent)
    {
        try
        {
            // Parse WiFi QR format: WIFI:T:WPA;S:MyNetwork;P:MyPassword;H:false;;
            var wifiInfo = ParseWifiQRCode(wifiContent);

            var message = $"WiFi Network Detected:\n" +
                         $"Network: {wifiInfo.SSID}\n" +
                         $"Security: {wifiInfo.Security}\n" +
                         $"Hidden: {wifiInfo.Hidden}";

            var action = await DisplayActionSheet(message, "Cancel", null, "Connect to WiFi", "Copy Password", "Show Details");

            switch (action)
            {
                case "Connect to WiFi":
                    await ConnectToWifi(wifiInfo);
                    break;
                case "Copy Password":
                    if (!string.IsNullOrEmpty(wifiInfo.Password))
                    {
                        await Clipboard.Default.SetTextAsync(wifiInfo.Password);
                        await DisplayAlert("Success", "WiFi password copied to clipboard", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Info", "No password available for this network", "OK");
                    }
                    break;
                case "Show Details":
                    await DisplayAlert("WiFi Details",
                        $"SSID: {wifiInfo.SSID}\n" +
                        $"Password: {wifiInfo.Password}\n" +
                        $"Security: {wifiInfo.Security}\n" +
                        $"Hidden: {wifiInfo.Hidden}", "OK");
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("WiFi Error", $"Failed to process WiFi QR code: {ex.Message}", "OK");
        }
    }

    private async Task HandlePhoneNumber(string phoneContent)
    {
        var phoneNumber = phoneContent.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            ? phoneContent.Substring(4)
            : phoneContent;

        var action = await DisplayActionSheet($"Phone Number: {phoneNumber}", "Cancel", null, "Call", "Send SMS", "Copy Number");

        switch (action)
        {
            case "Call":
                try
                {
                    PhoneDialer.Default.Open(phoneNumber);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to open dialer: {ex.Message}", "OK");
                }
                break;
            case "Send SMS":
                try
                {
                    var message = new SmsMessage("", phoneNumber);
                    await Sms.Default.ComposeAsync(message);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to open SMS: {ex.Message}", "OK");
                }
                break;
            case "Copy Number":
                await Clipboard.Default.SetTextAsync(phoneNumber);
                await DisplayAlert("Success", "Phone number copied to clipboard", "OK");
                break;
        }
    }

    private async Task HandleEmail(string emailContent)
    {
        var emailAddress = emailContent.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? emailContent.Substring(7)
            : emailContent;

        var action = await DisplayActionSheet($"Email: {emailAddress}", "Cancel", null, "Send Email", "Copy Email");

        switch (action)
        {
            case "Send Email":
                try
                {
                    var message = new EmailMessage
                    {
                        To = new List<string> { emailAddress },
                        Subject = "",
                        Body = ""
                    };
                    await Email.Default.ComposeAsync(message);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to open email: {ex.Message}", "OK");
                }
                break;
            case "Copy Email":
                await Clipboard.Default.SetTextAsync(emailAddress);
                await DisplayAlert("Success", "Email address copied to clipboard", "OK");
                break;
        }
    }

    private async Task HandleSms(string smsContent)
    {
        try
        {
            // Parse SMS format: sms:+1234567890?body=Hello
            var uri = new Uri(smsContent);
            var phoneNumber = uri.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var body = query["body"] ?? "";

            var action = await DisplayActionSheet($"SMS to: {phoneNumber}", "Cancel", null, "Send SMS", "Copy Number");

            switch (action)
            {
                case "Send SMS":
                    var message = new SmsMessage(body, phoneNumber);
                    await Sms.Default.ComposeAsync(message);
                    break;
                case "Copy Number":
                    await Clipboard.Default.SetTextAsync(phoneNumber);
                    await DisplayAlert("Success", "Phone number copied to clipboard", "OK");
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("SMS Error", $"Failed to process SMS: {ex.Message}", "OK");
        }
    }

    private async Task HandleGeoLocation(string geoContent)
    {
        try
        {
            double latitude, longitude;

            if (geoContent.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
            {
                // geo:latitude,longitude format
                var coords = geoContent.Substring(4).Split(',');
                latitude = double.Parse(coords[0]);
                longitude = double.Parse(coords[1]);
            }
            else
            {
                // Simple latitude,longitude format
                var coords = geoContent.Split(',');
                latitude = double.Parse(coords[0]);
                longitude = double.Parse(coords[1]);
            }

            var action = await DisplayActionSheet($"Location: {latitude:F6}, {longitude:F6}", "Cancel", null, "Open in Maps", "Copy Coordinates");

            switch (action)
            {
                case "Open in Maps":
                    var location = new Location(latitude, longitude);
                    var options = new MapLaunchOptions { Name = "Scanned Location" };
                    await Map.Default.OpenAsync(location, options);
                    break;
                case "Copy Coordinates":
                    await Clipboard.Default.SetTextAsync($"{latitude},{longitude}");
                    await DisplayAlert("Success", "Coordinates copied to clipboard", "OK");
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Location Error", $"Failed to process location: {ex.Message}", "OK");
        }
    }

    private async Task HandleVCard(string vCardContent)
    {
        try
        {
            var contactInfo = ParseVCard(vCardContent);

            var message = $"Contact Information:\n" +
                         $"Name: {contactInfo.Name}\n" +
                         $"Phone: {contactInfo.Phone}\n" +
                         $"Email: {contactInfo.Email}";

            var action = await DisplayActionSheet("Contact Detected", "Cancel", null, "Add to Contacts", "Copy Details");

            switch (action)
            {
                case "Add to Contacts":
                    // Note: Adding contacts requires platform-specific implementation
                    await DisplayAlert("Info", "Please manually add the contact with the provided information", "OK");
                    await DisplayAlert("Contact Details", message, "OK");
                    break;
                case "Copy Details":
                    await Clipboard.Default.SetTextAsync(message);
                    await DisplayAlert("Success", "Contact details copied to clipboard", "OK");
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("vCard Error", $"Failed to process contact: {ex.Message}", "OK");
        }
    }

    private async Task HandlePlainText(string content)
    {
        var action = await DisplayActionSheet($"Text Content: {content}", "Cancel", null, "Copy Text", "Share Text");

        switch (action)
        {
            case "Copy Text":
                await Clipboard.Default.SetTextAsync(content);
                await DisplayAlert("Success", "Text copied to clipboard", "OK");
                break;
            case "Share Text":
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Text = content,
                    Title = "Scanned QR Code Content"
                });
                break;
        }
    }

    private async Task ConnectToWifi(WifiInfo wifiInfo)
    {
        try
        {
            // This is a simplified approach - actual WiFi connection requires platform-specific code
            var message = $"To connect to WiFi network '{wifiInfo.SSID}':\n\n" +
                         $"1. Go to WiFi settings\n" +
                         $"2. Select network: {wifiInfo.SSID}\n" +
                         $"3. Enter password: {wifiInfo.Password}\n\n" +
                         $"Password has been copied to clipboard for convenience.";

            if (!string.IsNullOrEmpty(wifiInfo.Password))
            {
                await Clipboard.Default.SetTextAsync(wifiInfo.Password);
            }

            await DisplayAlert("WiFi Connection", message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("WiFi Error", $"Failed to prepare WiFi connection: {ex.Message}", "OK");
        }
    }

    private WifiInfo ParseWifiQRCode(string wifiContent)
    {
        var wifiInfo = new WifiInfo();

        // Parse WIFI:T:WPA;S:MyNetwork;P:MyPassword;H:false;;
        var parts = wifiContent.Split(';');

        foreach (var part in parts)
        {
            if (part.StartsWith("S:", StringComparison.OrdinalIgnoreCase))
                wifiInfo.SSID = part.Substring(2);
            else if (part.StartsWith("P:", StringComparison.OrdinalIgnoreCase))
                wifiInfo.Password = part.Substring(2);
            else if (part.StartsWith("T:", StringComparison.OrdinalIgnoreCase))
                wifiInfo.Security = part.Substring(2);
            else if (part.StartsWith("H:", StringComparison.OrdinalIgnoreCase))
                wifiInfo.Hidden = part.Substring(2).ToLower() == "true";
        }

        return wifiInfo;
    }

    private ContactInfo ParseVCard(string vCardContent)
    {
        var contact = new ContactInfo();
        var lines = vCardContent.Split('\n');

        foreach (var line in lines)
        {
            if (line.StartsWith("FN:", StringComparison.OrdinalIgnoreCase))
                contact.Name = line.Substring(3);
            else if (line.StartsWith("TEL:", StringComparison.OrdinalIgnoreCase))
                contact.Phone = line.Substring(4);
            else if (line.StartsWith("EMAIL:", StringComparison.OrdinalIgnoreCase))
                contact.Email = line.Substring(6);
        }

        return contact;
    }

    public class WifiInfo
    {
        public string SSID { get; set; } = "";
        public string Password { get; set; } = "";
        public string Security { get; set; } = "";
        public bool Hidden { get; set; } = false;
    }

    public class ContactInfo
    {
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
    }

    protected override void OnDisappearing()
    {
        try
        {
            if (_dynamicCameraView != null && _dynamicCameraView.IsDetecting)
            {
                _dynamicCameraView.IsDetecting = false;
            }
            IsScanning = false;

            // Clean up dynamic camera view
            RemoveDynamicCameraView();
        }
        catch { /* Ignore cleanup errors */ }
        base.OnDisappearing();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}