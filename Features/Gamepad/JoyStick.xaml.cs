using Base.Mathf;
using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gamepad
{
    /// <summary>
    /// Interaction logic for JoyStick.xaml
    /// </summary>
    public partial class JoyStick : UserControl
    {

        public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(JoyStick),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(JoyStick),
                new PropertyMetadata("L"));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public JoyStick()
        {
            InitializeComponent();

            // cache the segment
            var geo = (PathGeometry)(DirectionLine).Data;
            var fig = geo.Figures[0];
            directionSegment = (LineSegment)fig.Segments[0];

            // circularity line
            Path circularityPath = CircularityLine;
            var circularityGeo = (PathGeometry)(circularityPath).Data;
            circularityPathFigure = circularityGeo.Figures[0];
            Point center = circularityPathFigure.StartPoint;
            for (int i = 0; i < segments; i++)
            {
                double angle = (double)i / segments * 2.0 * Math.PI;
                var pt = ToPoint(center, 0, angle);
                var seg = new LineSegment(pt, true);
                circularityPathFigure.Segments.Add(seg);
                lineSegments[i] = seg;
            }
            circularityPathFigure.IsClosed = true;

            SizeChanged += (_, __) => RecalcRadius();
            Loaded += (_, __) => UpdateVisual();

            Clear();
        }

        private void RecalcRadius()
        {
            radiusPx = 80.0;
            UpdateVisual();
            UpdateCircularityLine();
        }

        private void UpdateVisual()
        {
            // deadzone
            var sx = stickX;
            var sy = stickY;
            var mag = Math.Sqrt(sx * sx + sy * sy);

            var px = sx * radiusPx;
            var py = -sy * radiusPx; // WPF Y+ down -> invert

            directionSegment.Point = new Point(px, py);
            Canvas.SetLeft(CenterPoint, px);
            Canvas.SetTop(CenterPoint, py);
        }

        public void SetStick(int x, int y)
        {
            stickX = x / 32767f;
            stickY = -y / 32767f;
            XValueText.Text = $"{x: #;-#; 0}";
            YValueText.Text = $"{y: #;-#; 0}";

            if (x < xMin) { xMin = x; XValueMinText.Text = xMin.ToString(" #;-#; 0"); }
            if (x > xMax) { xMax = x; XValueMaxText.Text = xMax.ToString(" #;-#; 0"); }
            if (y < yMin) { yMin = y; YValueMinText.Text = yMin.ToString(" #;-#; 0"); }
            if (y > yMax) { yMax = y; YValueMaxText.Text = yMax.ToString(" #;-#; 0"); }

            int currentSegment = CalculateSegmentIndex(x, y);
            float currentMagnitude = UnitMagnitude(x, y);
            if (currentSegment >= 0)
            {
                segmentMagnitudes[currentSegment] = Math.Max(segmentMagnitudes[currentSegment], currentMagnitude);
                ApplyArcInterpolation(lastSegment, currentSegment, currentMagnitude);
                lastMagnitude = segmentMagnitudes[currentSegment];
            }

            double circularity = CalculateCircularity();
            CircularityText.Text = circularity.ToString("P2");
            MagnitudeText.Text = currentMagnitude.ToString("P2");

            UpdateVisual();
            UpdateCircularityLine();
            lastSegment = currentSegment;
        }

        private void ApplyArcInterpolation(int lastSegment, int segment, float currentMagnitude)
        {
            if (lastSegment == segment || lastSegment < 0) return;

            int turnDirection = TurnDirection(lastSegment, segment);
            int steps = (turnDirection * (segment - lastSegment)) & (segments - 1);

            Vector2 startPos = dirs[lastSegment] * lastMagnitude;
            Vector2 endPos = dirs[segment] * currentMagnitude;
            Vector2 direction = endPos - startPos;

            for (int i = lastSegment; i != segment; i = (i + turnDirection) & (segments - 1))
            {
                if (Raycast2D.RayIntersectionFromOrigin(dirs[i], startPos, direction, out float distance))
                {
                    if (segmentMagnitudes[i] > distance) continue;
                    segmentMagnitudes[i] = distance;
                }
            }
        }

        private float UnitMagnitude(int x, int y)
        {
            return MathF.Sqrt(x * x + y * y) / 32767f;
        }

        // Get segment index by calculating it's angle
        private int CalculateSegmentIndex(int x, int y)
        {
            if (x == 0 && y == 0) return -1; // center
            double angle = Math.Atan2(y, x); // -PI..PI
            if (angle < 0) angle += 2 * Math.PI; // 0..2PI
            int index = (int)(angle / (2 * Math.PI) * segments);
            if (index >= segments) index = segments - 1;
            return index;
        }

        private double CalculateCircularity()
        {
            double sum = 0.0;
            for (int i = 0; i < segments; i++)
            {
                sum += segmentMagnitudes[i];
            }
            return sum / segments; // average
        }

        private void UpdateCircularityLine()
        {
            double sum = 0.0;
            for (int i = 0; i < segments; i++)
            {
                double r = segmentMagnitudes[i] * radiusPx;
                sum += segmentMagnitudes[i];
                var pt = ToPoint(new Point(0, 0), r, (double)i / segments * 2.0 * Math.PI);
                lineSegments[i].Point = pt;
            }
            circularityPathFigure.StartPoint = lineSegments[0].Point;
        }

        private int TurnDirection(int last, int current)
        {
            int d = (current - last) & (segments - 1);
            if (d == 0) return 0;
            if (d < segments / 2) return 1; // CW
            if (d > segments / 2) return -1; // CCW
            return 2;
        }

        private static Point ToPoint(in Point c, double r, double angleRad)
        {
            double x = c.X + Math.Cos(angleRad) * r;
            double y = c.Y + Math.Sin(angleRad) * r; // WPF Y+ is down; this keeps 0° at +X, CCW positive
            return new Point(x, y);
        }

        public void Clear()
        {
            xMin = int.MaxValue; XValueMinText.Text = " --";
            xMax = int.MinValue; XValueMaxText.Text = " --";
            yMin = int.MaxValue; YValueMinText.Text = " --";
            yMax = int.MinValue; YValueMaxText.Text = " --";
            XValueText.Text = " --";
            YValueText.Text = " --";
            MagnitudeText.Text = " --";
            CircularityText.Text = " --";

            for (int i = 0; i < segments; i++)
            {
                segmentMagnitudes[i] = 0f;

                float angle = (float)i / segments * 2.0f * MathF.PI;
                dirs[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            stickX = 0;
            stickY = 0;
            lastSegment = -1;
            lastMagnitude = 0f;
            UpdateCircularityLine();
        }

        private readonly LineSegment directionSegment;
        private double radiusPx = 80.0;

        private double stickX;
        private double stickY;

        private int xMin = 0;
        private int xMax = 0;
        private int yMin = 0;
        private int yMax = 0;

        // Circularity segments
        private const int segments = 64;
        private int lastSegment = -1;
        private float lastMagnitude = 0f;
        private readonly float[] segmentMagnitudes = new float[segments];
        private readonly Vector2[] dirs = new Vector2[segments];
        private readonly LineSegment[] lineSegments = new LineSegment[segments];
        private readonly PathFigure circularityPathFigure;
    }
}