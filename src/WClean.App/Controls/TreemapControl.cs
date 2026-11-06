using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WClean.App.Controls;

/// <summary>Draws DiskItem sizes as a squarified treemap. Double-click opens a folder tile.</summary>
public sealed class TreemapControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((TreemapControl)d).OnItemsChanged(e)));

    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(
        nameof(OpenCommand), typeof(ICommand), typeof(TreemapControl));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x14, 0xB8, 0xA6),
        Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x22, 0xC5, 0x5E),
        Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0x64, 0x74, 0x8B),
    };

    private List<(Rect Rect, DiskItem Item)> _tiles = new();
    private DiskItem? _hover;

    public TreemapControl()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    private void OnItemsChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged o) o.CollectionChanged -= OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged n) n.CollectionChanged += OnCollectionChanged;
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(InvalidateVisual));

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var bg = (Brush)(TryFindResource("SurfaceBrush") ?? Brushes.Transparent);
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));   // also makes the whole area hit-testable
        _tiles = new();

        var items = ItemsSource?.OfType<DiskItem>().Where(i => i.Size > 0).ToList() ?? new();
        if (items.Count == 0 || w < 10 || h < 10) return;

        var rects = DiskUsageService.Layout(items.Select(i => i.Size).ToList(), w, h);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var textBrush = Brushes.White;

        foreach (var r in rects)
        {
            var item = items[r.Index];
            var rect = new Rect(r.X + 1, r.Y + 1, Math.Max(0, r.W - 2), Math.Max(0, r.H - 2));
            if (rect.Width < 2 || rect.Height < 2) continue;
            _tiles.Add((rect, item));

            var c = Palette[r.Index % Palette.Length];
            if (!item.IsDirectory) c = Color.FromRgb((byte)(c.R * 0.55 + 90), (byte)(c.G * 0.55 + 90), (byte)(c.B * 0.55 + 90));
            var fill = new SolidColorBrush(item == _hover ? Lighten(c) : c);
            dc.DrawRoundedRectangle(fill, null, rect, 6, 6);

            if (rect.Width > 70 && rect.Height > 38)
            {
                var title = new FormattedText(item.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, textBrush, dpi)
                { MaxTextWidth = rect.Width - 16, MaxTextHeight = 20, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(title, new Point(rect.X + 8, rect.Y + 6));
                var size = new FormattedText(item.SizeText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 12, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), dpi)
                { MaxTextWidth = rect.Width - 16, MaxTextHeight = 18, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(size, new Point(rect.X + 8, rect.Y + 24));
            }
        }
    }

    private static Color Lighten(Color c) => Color.FromRgb((byte)Math.Min(255, c.R + 28), (byte)Math.Min(255, c.G + 28), (byte)Math.Min(255, c.B + 28));

    private DiskItem? HitTest(Point p) => _tiles.FirstOrDefault(t => t.Rect.Contains(p)).Item;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var item = HitTest(e.GetPosition(this));
        if (!ReferenceEquals(item, _hover))
        {
            _hover = item;
            ToolTip = item is null ? null : $"{item.Path}\n{item.SizeText}" + (item.IsDirectory ? "  ·  두 번 클릭하면 열기" : "");
            Cursor = item is { IsDirectory: true } ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover is not null) { _hover = null; InvalidateVisual(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && HitTest(e.GetPosition(this)) is { IsDirectory: true } item && OpenCommand?.CanExecute(item) == true)
            OpenCommand.Execute(item);
        base.OnMouseLeftButtonDown(e);
    }
}
