using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WClean.App;

/// <summary>Small notification in the bottom-right corner (used after a scheduled clean).</summary>
public sealed class ToastWindow : Window
{
    public ToastWindow(string title, string message, TimeSpan duration)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MinWidth = 260 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = message, FontSize = 13, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)) });
        Content = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, Opacity = 0.35, ShadowDepth = 3 },
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        };
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
