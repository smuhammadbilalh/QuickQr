using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;
using CommunityToolkit.Maui;

namespace QuickQr
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader() // Add ZXing barcode reader support
                .UseMauiCommunityToolkit() // Add this line

                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("fontawesome.otf", "FontAwesome"); // Add this line
                    fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons"); // Ensure this line exists
                });


#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}