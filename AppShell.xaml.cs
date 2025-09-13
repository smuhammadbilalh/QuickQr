namespace QuickQr;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation - MUST be done before any navigation
        Routing.RegisterRoute("MainPage", typeof(MainPage));
        Routing.RegisterRoute("ScanPage", typeof(ScanPage));
        Routing.RegisterRoute("DeveloperPage", typeof(DeveloperPage));
        CreateQRMenu.BackgroundColor = Colors.SkyBlue;
        appVersion.Text = $"v{AppInfo.Current.VersionString}"; // Display app version in the UI

    }

    // Navigation with duplicate prevention
    private async void OnCreateQRTapped(object sender, EventArgs e)
    {
        CreateQRMenu.BackgroundColor = Colors.SkyBlue;
        ScanQRMenu.BackgroundColor = Colors.Transparent;
        DeveloperMenu.BackgroundColor = Colors.Transparent;
        try
        {
            // Close flyout first for smooth experience
            FlyoutIsPresented = false;

            // Check if we're already on the Create QR page
            if (Current.CurrentState.Location.OriginalString.Contains("MainPage"))
            {
                return; // Already on this page, don't navigate
            }

            // Navigate to MainPage (Create QR functionality)
            await Shell.Current.GoToAsync("//MainPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation Error", "Unable to navigate to Create QR page.", "OK");
        }
    }

    private async void OnScanQRTapped(object sender, EventArgs e)
    {
        CreateQRMenu.BackgroundColor = Colors.Transparent;
        ScanQRMenu.BackgroundColor = Colors.SkyBlue;
        DeveloperMenu.BackgroundColor = Colors.Transparent;
        try
        {
            // Close flyout first for smooth experience
            FlyoutIsPresented = false;

            // Check if we're already on the Scan QR page
            if (Current.CurrentState.Location.OriginalString.Contains("ScanPage"))
            {
                return; // Already on this page, don't navigate
            }

            // Navigate to ScanPage
            await Shell.Current.GoToAsync("//ScanPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation Error", "Unable to navigate to Scan QR page.", "OK");
        }
    }

    private async void OnDeveloperTapped(object sender, EventArgs e)
    {
        CreateQRMenu.BackgroundColor = Colors.Transparent;
        ScanQRMenu.BackgroundColor = Colors.Transparent;
        DeveloperMenu.BackgroundColor = Colors.SkyBlue;
        try
        {
            // Close flyout first for smooth experience
            FlyoutIsPresented = false;

            // Check if we're already on the Developer page
            if (Current.CurrentState.Location.OriginalString.Contains("DeveloperPage"))
            {
                return; // Already on this page, don't navigate
            }

            // Navigate to DeveloperPage
            await Shell.Current.GoToAsync("//DeveloperPage");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation Error", "Unable to navigate to Developer tools.", "OK");
        }
    }

    private async void OnDemoClicked(object sender, EventArgs e)
    {
        try
        {
            // Replace with your actual Google Drive video URL
            string demoVideoUrl = "https://drive.google.com/file/d/1yigcKBX3uGF0xIOzrXWMVxeoxgeN-Y2r/view?usp=sharing";
            await Browser.Default.OpenAsync(demoVideoUrl, BrowserLaunchMode.SystemPreferred);
            // Close flyout smoothly
            FlyoutIsPresented = false;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", "Unable to open demo video. Please check your internet connection.", "OK");
        }
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        try
        {
            // Close flyout first for smooth experience
            FlyoutIsPresented = false;

            // Get the App instance and call manual update check
            if (Application.Current is App app)
            {
                await app.CheckForUpdatesManually();
            }
            else
            {
                await DisplayAlert("Error", "Unable to check for updates at this time.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Update Error", "Failed to check for updates. Please try again later.", "OK");
        }
    }

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        FlyoutIsPresented = false;

        var aboutInfo = new
        {
            Name = AppInfo.Current.Name,
            Version = AppInfo.Current.VersionString,
            Build = AppInfo.Current.BuildString,
            Developer = "Muhammad Bilal",
            Company = "QuickQR Solutions",
            Email = "bilal@quickqr.com"
        };

        string message = $"📱 {aboutInfo.Name}\n" +
                        $"🏷️ Version {aboutInfo.Version} (Build {aboutInfo.Build})\n\n" +
                        $"👨‍💻 Developed by {aboutInfo.Developer}\n" +
                        $"🏢 {aboutInfo.Company}\n" +
                        $"📧 smuhammadbilalh@gmail.com\n\n" +
                        $"© 2025 All Rights Reserved";

        await DisplayAlert("About QuickQR", message, "Close");
    }
}