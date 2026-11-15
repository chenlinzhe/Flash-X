using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FolderScope.Core;

namespace FolderScope.Desktop;

public sealed class SizeChart : Canvas
{
    private IReadOnlyList<ScanNode> _nodes = [];
    private readonly List<(Geometry Shape, ScanNode Node)> _hits = [];
    public bool Bars { get; set; }
    public bool HasData => _nodes.Count > 0;
    public event Action<ScanNode>? Selected;
    public SizeChart()
    {
        Background = Brushes.White;
        MinHeight = 140;
        SizeChanged += (_, _) => InvalidateVisual();
        MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(this);
            foreach (var (shape, node) in _hits)
                if (shape.FillContains(point)) { Selected?.Invoke(node); break; }
        };
        MouseMove += (_, e) =>
        {
            var point = e.GetPosition(this);
            ToolTip = _hits.Where(h => h.Shape.FillContains(point)).Select(h => h.Node.Name + " · " + ScanWindow.FormatBytes(h.Node.TotalBytes)).FirstOrDefault();
        };
    }
    public void SetNodes(IEnumerable<ScanNode> nodes)
        {
        _nodes = ChartSummary.Create(nodes);

        InvalidateVisual();
    }
    public void SetSummary(IReadOnlyList<ScanNode> summary) { _nodes = summary; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear();
        var total = _nodes.Sum(n => (double)n.TotalBytes);
        if (total == 0 || ActualWidth < 30 || ActualHeight < 30) return;
        var colors = new[] { Colors.ForestGreen, Colors.Coral, Colors.SteelBlue, Colors.Goldenrod, Colors.MediumPurple, Colors.Teal, Colors.HotPink, Colors.Olive };
        var shown = _nodes;
        var largest = shown.Max(n => n.TotalBytes);
        var radius = Math.Min(ActualHeight / 2 - 8, ActualWidth * .25);
        var center = new Point(radius + 10, ActualHeight / 2);
        var angle = -Math.PI / 2;
        for (var i = 0; i < shown.Count; i++)
        {
            var node = shown[i]; var fraction = node.TotalBytes / total;
            var brush = new SolidColorBrush(node.Id < 0 ? Colors.Gray : colors[i % colors.Length]); Geometry shape;
            if (Bars)
                shape = new RectangleGeometry(new Rect(8, 8 + i * (ActualHeight - 16) / shown.Count, Math.Max(1, (ActualWidth * .5 - 16) * node.TotalBytes / largest), Math.Max(3, (ActualHeight - 16) / shown.Count - 5)));
            else if (fraction >= .999999)
                shape = new EllipseGeometry(center, radius, radius);
            else
            {
                var end = angle + fraction * Math.PI * 2;
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(center, true, true);
                    context.LineTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), true, false);
                    context.ArcTo(new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
                }
                shape = geometry; angle = end;
            }
            dc.DrawGeometry(brush, new Pen(Brushes.White, 1), shape);
            if (node.Id >= 0) _hits.Add((shape, node));
            var text = new FormattedText($"{node.Name}  {fraction:P1}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Tahoma"), 12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            text.MaxTextWidth = Math.Max(1, ActualWidth * .48 - 12);
            dc.DrawRectangle(brush, null, new Rect(ActualWidth * .52, 10 + i * 20, 10, 10));
            dc.DrawText(text, new Point(ActualWidth * .52 + 15, 7 + i * 20));
        }
    }
}
