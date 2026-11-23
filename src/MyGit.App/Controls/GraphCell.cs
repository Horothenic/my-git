using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MyGit.Core.History;

namespace MyGit.App.Controls;

/// <summary>
/// Draws the commit graph for one row of the History list: the lines passing through,
/// the lines joining this commit, and its dot. Lanes are <see cref="LaneWidth"/> apart.
/// </summary>
public sealed class GraphCell : Control
{
    public const double LaneWidth = 14;

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphCell, GraphRow?>(nameof(Row));

    // One color per lane, repeating. Chosen to read well on both dark and light backgrounds.
    private static readonly IBrush[] LaneBrushes =
    [
        new SolidColorBrush(Color.Parse("#378ADD")),
        new SolidColorBrush(Color.Parse("#1D9E75")),
        new SolidColorBrush(Color.Parse("#D85A30")),
        new SolidColorBrush(Color.Parse("#7F77DD")),
        new SolidColorBrush(Color.Parse("#BA7517")),
        new SolidColorBrush(Color.Parse("#D4537E")),
    ];

    static GraphCell()
    {
        AffectsRender<GraphCell>(RowProperty);
        AffectsMeasure<GraphCell>(RowProperty);
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new((Row?.LaneCount ?? 1) * LaneWidth + 4, 0);

    public override void Render(DrawingContext context)
    {
        if (Row is not { } row)
        {
            return;
        }

        var height = Bounds.Height;
        var middle = height / 2;

        foreach (var edge in row.Edges)
        {
            // A line starts in the lane it comes from, so its color follows that lane.
            var pen = new Pen(BrushFor(edge.Half == GraphHalf.Top ? edge.From : edge.To), 2);

            var start = edge.Half == GraphHalf.Top ? new Point(X(edge.From), 0) : new Point(X(edge.From), middle);
            var end = edge.Half == GraphHalf.Top ? new Point(X(edge.To), middle) : new Point(X(edge.To), height);

            if (start.X == end.X)
            {
                context.DrawLine(pen, start, end);
            }
            else
            {
                // A soft curve between lanes reads better than a diagonal.
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(start, isFilled: false);
                    g.CubicBezierTo(new Point(start.X, (start.Y + end.Y) / 2), new Point(end.X, (start.Y + end.Y) / 2), end);
                    g.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, pen, geometry);
            }
        }

        var node = new Point(X(row.NodeLane), middle);
        context.DrawEllipse(BrushFor(row.NodeLane), new Pen(Brushes.Transparent), node, 4.5, 4.5);
    }

    private static double X(int lane) => lane * LaneWidth + LaneWidth / 2 + 2;

    private static IBrush BrushFor(int lane) => LaneBrushes[lane % LaneBrushes.Length];
}
