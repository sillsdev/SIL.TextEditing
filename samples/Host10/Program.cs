using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SIL.TextEditing.Samples;

namespace SIL.TextEditing.Samples.Host10
{
    sealed class HostApp : SampleApp
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new Window
                {
                    Title = "SIL.TextEditing sample (net10.0, Avalonia 12)",
                    Width = 520,
                    Height = 480,
                    Content = new SampleUi().Root,
                };
            base.OnFrameworkInitializationCompleted();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args) =>
            AppBuilder.Configure<HostApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
