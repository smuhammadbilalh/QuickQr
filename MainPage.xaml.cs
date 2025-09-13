using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using ZXing;
using ZXing.Common;

namespace QuickQr;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private string _inputText = "";
    private byte[]? _currentQrCodeBytes; // Store the current QR code as bytes

    public string InputText
    {
        get => _inputText;
        set
        {
            _inputText = value;
            OnPropertyChanged();
        }
    }

    public MainPage()
    {
        InitializeComponent();

        // Check if this is the first run and show notification
        CheckAndShowFirstRunNotification();

        BindingContext = this;
        QrCodeImage.IsVisible = false; // Add this line
        SetupToolbarItems();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();


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

    // Replace your CheckAndShowFirstRunNotification method with this simple version:
    private async void CheckAndShowFirstRunNotification()
    {
        const string FIRST_RUN_KEY = "IsFirstRun";

        try
        {
            bool isFirstRun = Preferences.Get(FIRST_RUN_KEY, true);

            if (isFirstRun)
            {
                // Wait a bit for the app to fully load
                await Task.Delay(1500);

                // Show a welcome alert instead of notification
                await DisplayAlert(
                    "🎉 Welcome to QuickQR!",
                    "Crafted with ❤️ by Muhammad Bilal.\n\nReady to create amazing QR codes? Let's get started!",
                    "Let's Go!");

                Preferences.Set(FIRST_RUN_KEY, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Welcome message error: {ex.Message}");
            Preferences.Set(FIRST_RUN_KEY, false);
        }
    }


    private void SetupToolbarItems()
    {
        // Create the dropdown button
        var addButton = new ToolbarItem
        {
            Order = ToolbarItemOrder.Primary,
            Priority = 0,

            IconImageSource = new FontImageSource
            {
                Glyph = "\ue145", // Unicode for Material "add" icon
                FontFamily = "MaterialIcons",
                Size = 30,
                Color = Colors.BlueViolet // Or your preferred color
            }
        };
        // Handle the toolbar item click to show action sheet
        addButton.Clicked += async (s, e) => await ShowQRTypeMenu();

        // Add the toolbar item to the page
        ToolbarItems.Add(addButton);
    }

    private async Task ShowQRTypeMenu()
    {
        var action = await DisplayActionSheet("Select QR Code Type", "Cancel", null,
            "Email", "Phone", "Website", "SMS", "WiFi", "Contact", "Location");

        switch (action)
        {
            case "Email":
                OnMenuItemClicked("mailto:");
                break;
            case "Phone":
                OnMenuItemClicked("tel:");
                break;
            case "Website":
                OnMenuItemClicked("https://");
                break;
            case "SMS":
                OnMenuItemClicked("sms:");
                break;
            case "WiFi":
                await OnWiFiMenuItemClicked();
                break;
            case "Contact":
                await OnContactMenuItemClicked();
                break;
            case "Location":
                OnMenuItemClicked("geo:");
                break;
        }
    }

    private void OnMenuItemClicked(string prefix)
    {
        // Get current text and cursor position
        TextEntry.Text = string.Empty;
        var currentText = TextEntry.Text ?? "";
        var cursorPosition = TextEntry.CursorPosition;

        // Insert the prefix at the cursor position
        var newText = currentText.Insert(cursorPosition, prefix);

        // Update the text and move cursor after the prefix
        TextEntry.Text = newText;
        TextEntry.CursorPosition = cursorPosition + prefix.Length;

        // Focus the entry for immediate typing
        TextEntry.Focus();
    }

    private async Task OnWiFiMenuItemClicked()
    {
        // Show dialog to get WiFi details
        var networkName = await DisplayPromptAsync("WiFi Network", "Enter network name (SSID):");
        if (string.IsNullOrWhiteSpace(networkName))
            return;

        var password = await DisplayPromptAsync("WiFi Password", "Enter password (leave empty for open network):");
        var security = string.IsNullOrWhiteSpace(password) ? "nopass" : "WPA";

        // WiFi QR code format: WIFI:T:WPA;S:NetworkName;P:Password;H:false;;
        var wifiString = $"WIFI:T:{security};S:{networkName};P:{password ?? ""};H:false;;";

        var currentText = TextEntry.Text ?? "";
        var cursorPosition = TextEntry.CursorPosition;
        var newText = currentText.Insert(cursorPosition, wifiString);

        TextEntry.Text = newText;
        TextEntry.CursorPosition = cursorPosition + wifiString.Length;
        TextEntry.Focus();
    }

    private async Task OnContactMenuItemClicked()
    {
        // Show dialog to get contact details
        var name = await DisplayPromptAsync("Contact", "Enter full name:");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var phone = await DisplayPromptAsync("Contact", "Enter phone number (optional):");
        var email = await DisplayPromptAsync("Contact", "Enter email (optional):");

        // vCard format
        var vcard = $"BEGIN:VCARD\nVERSION:3.0\nFN:{name}\n";
        if (!string.IsNullOrWhiteSpace(phone))
            vcard += $"TEL:{phone}\n";
        if (!string.IsNullOrWhiteSpace(email))
            vcard += $"EMAIL:{email}\n";
        vcard += "END:VCARD";

        var currentText = TextEntry.Text ?? "";
        var cursorPosition = TextEntry.CursorPosition;
        var newText = currentText.Insert(cursorPosition, vcard);

        TextEntry.Text = newText;
        TextEntry.CursorPosition = cursorPosition + vcard.Length;
        TextEntry.Focus();
    }



    // Method 2: Enhanced QR generation with rendering isolation and proper margins
    private byte[]? GenerateQRCodeBytes(string text)
    {
        try
        {
            // Generate QR code using ZXing.Net
            var writer = new ZXing.QrCode.QRCodeWriter();
            var hints = new Dictionary<ZXing.EncodeHintType, object>
        {
            { ZXing.EncodeHintType.ERROR_CORRECTION, ZXing.QrCode.Internal.ErrorCorrectionLevel.H },
            { ZXing.EncodeHintType.MARGIN, 4 }, // Increased margin for better padding
            { ZXing.EncodeHintType.CHARACTER_SET, "UTF-8" }
        };

            var bitMatrix = writer.encode(text, ZXing.BarcodeFormat.QR_CODE, 500, 500, hints);

            // CRITICAL: Create bitmap in completely isolated context with proper padding
            var qrWidth = bitMatrix.Width;
            var qrHeight = bitMatrix.Height;

            // Add extra padding around the QR code (20px on each side)
            var padding = 20;
            var totalWidth = qrWidth + (padding * 2);
            var totalHeight = qrHeight + (padding * 2);

            // Use a different approach that avoids MAUI's rendering pipeline issues
            using var surface = SKSurface.Create(new SKImageInfo(totalWidth, totalHeight));
            using var canvas = surface.Canvas;

            // Clear with white background
            canvas.Clear(SKColors.White);

            // Draw QR code pixel by pixel using canvas with padding offset
            using var blackPaint = new SKPaint { Color = SKColors.Black };

            for (int y = 0; y < qrHeight; y++)
            {
                for (int x = 0; x < qrWidth; x++)
                {
                    if (bitMatrix[x, y])
                    {
                        canvas.DrawPoint(x + padding, y + padding, blackPaint);
                    }
                }
            }

            // Ensure drawing is complete
            canvas.Flush();

            // Get image from surface
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR Code Generation Error: {ex}");
            return null;
        }
    }

    // Method 3: Alternative approach using base64 encoding (sometimes more reliable)
    private async void OnGenerateClicked(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TextEntry.Text))
            {
                await DisplayAlert("Error", "Please enter text to generate QR code", "OK");
                return;
            }

            // Same setup as before...
            TextEntry.Unfocus();
            TextEntry.IsEnabled = false;
            GenerateButton.IsEnabled = false;
            LoadingOverlay.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            // Clear image completely
            QrCodeImage.Source = null;
            QrCodeImage.IsVisible = false;
            await Task.Delay(100);

            var qrCodeBytes = await Task.Run(() => GenerateQRCodeBytes(TextEntry.Text));

            if (qrCodeBytes != null)
            {
                _currentQrCodeBytes = qrCodeBytes;

                // Use base64 approach which sometimes avoids rendering issues
                var base64String = Convert.ToBase64String(qrCodeBytes);
                var dataUri = $"data:image/png;base64,{base64String}";

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    // Try using UriImageSource instead of StreamImageSource
                    QrCodeImage.Source = new UriImageSource
                    {
                        Uri = new Uri(dataUri),
                        CachingEnabled = false // Disable caching to avoid stale renders
                    };

                    await Task.Delay(100);
                    QrCodeImage.IsVisible = true;

                    LoadingOverlay.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                    TextEntry.IsEnabled = true;
                    GenerateButton.IsEnabled = true;
                });

                await DisplayAlert("Success", "QR Code generated successfully!", "OK");
            }
        }
        catch (Exception ex)
        {
            // Error handling...
            LoadingOverlay.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            TextEntry.IsEnabled = true;
            GenerateButton.IsEnabled = true;
            await DisplayAlert("Error", $"Failed to generate QR code: {ex.Message}", "OK");
        }
    }
    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentQrCodeBytes == null)
            {
                await DisplayAlert("Error", "Please generate a QR code first", "OK");
                return;
            }

            // Create a temporary file
            var fileName = $"QRCode_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            await File.WriteAllBytesAsync(filePath, _currentQrCodeBytes);

            // Save to photo library
            var mediaPickerService = MediaPicker.Default;

            // For Android and iOS, we need to use the platform-specific save to gallery
            // This approach works across platforms
            await SaveToPhotoLibrary(filePath);

            await DisplayAlert("Success", "QR Code saved to photo library!", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save QR code: {ex.Message}", "OK");
        }
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentQrCodeBytes == null)
            {
                await DisplayAlert("Error", "Please generate a QR code first", "OK");
                return;
            }

            // Create a temporary file
            var fileName = $"QRCode_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            await File.WriteAllBytesAsync(filePath, _currentQrCodeBytes);

            // Share the file
            var shareRequest = new ShareFileRequest
            {
                Title = "Share QR Code",
                File = new ShareFile(filePath)
            };

            await Share.Default.RequestAsync(shareRequest);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to share QR code: {ex.Message}", "OK");
        }
    }

    private async Task SaveToPhotoLibrary(string filePath)
    {
        try
        {
            // Request permission to save to photo library
            var status = await Permissions.RequestAsync<Permissions.Photos>();

            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Denied", "Permission to access photo library is required to save the QR code.", "OK");
                return;
            }

#if ANDROID
            // For Android, copy to Pictures directory
            var picturesPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryPictures);
            var androidFilePath = Path.Combine(picturesPath.AbsolutePath, "QRCodes", Path.GetFileName(filePath));

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(androidFilePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.Copy(filePath, androidFilePath, true);

            // Notify media scanner
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            Android.Media.MediaScannerConnection.ScanFile(context, new string[] { androidFilePath }, null, null);
#endif
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save to photo library: {ex.Message}");
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => !(bool)value;
}