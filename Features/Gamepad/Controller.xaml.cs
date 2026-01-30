// Controller.xaml.cs
using ModernWpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gamepad
{
    public partial class Controller : UserControl
    {
        public Controller()
        {
            InitializeComponent();
        }

        public double StickMaxOffset
        {
            get => (double)GetValue(StickMaxOffsetProperty);
            set => SetValue(StickMaxOffsetProperty, value);
        }

        public static readonly DependencyProperty StickMaxOffsetProperty =
            DependencyProperty.Register(nameof(StickMaxOffset), typeof(double), typeof(Controller),
                new PropertyMetadata(18.0));

        public static readonly DependencyProperty PressedBrushProperty =
            DependencyProperty.Register(nameof(PressedBrush), typeof(SolidColorBrush), typeof(Controller),
                new PropertyMetadata(Brushes.Black));

        public static readonly DependencyProperty ReleasedBrushProperty =
            DependencyProperty.Register(nameof(ReleasedBrush), typeof(SolidColorBrush), typeof(Controller),
                new PropertyMetadata(Brushes.White));

        public SolidColorBrush PressedBrush
        {
            get => (SolidColorBrush)GetValue(PressedBrushProperty);
            set => SetValue(PressedBrushProperty, value);
        }

        public SolidColorBrush ReleasedBrush
        {
            get => (SolidColorBrush)GetValue(ReleasedBrushProperty);
            set => SetValue(ReleasedBrushProperty, value);
        }

        // Face Buttons
        public bool A { get => (bool)GetValue(AProperty); set => SetValue(AProperty, value); }
        public static readonly DependencyProperty AProperty =
            DependencyProperty.Register(nameof(A), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnA_Bottom, (bool)e.NewValue)));

        public bool B { get => (bool)GetValue(BProperty); set => SetValue(BProperty, value); }
        public static readonly DependencyProperty BProperty =
            DependencyProperty.Register(nameof(B), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnB_Right, (bool)e.NewValue)));

        public bool X { get => (bool)GetValue(XProperty); set => SetValue(XProperty, value); }
        public static readonly DependencyProperty XProperty =
            DependencyProperty.Register(nameof(X), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnX_Left, (bool)e.NewValue)));

        public bool Y { get => (bool)GetValue(YProperty); set => SetValue(YProperty, value); }
        public static readonly DependencyProperty YProperty =
            DependencyProperty.Register(nameof(Y), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnY_Top, (bool)e.NewValue)));

        // Shoulders / Triggers
        public bool LB { get => (bool)GetValue(LBProperty); set => SetValue(LBProperty, value); }
        public static readonly DependencyProperty LBProperty =
            DependencyProperty.Register(nameof(LB), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).LBShape, (bool)e.NewValue)));

        public bool RB { get => (bool)GetValue(RBProperty); set => SetValue(RBProperty, value); }
        public static readonly DependencyProperty RBProperty =
            DependencyProperty.Register(nameof(RB), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).RBShape, (bool)e.NewValue)));

        public float LT { get => (float)GetValue(LTProperty); set => SetValue(LTProperty, value); }
        public static readonly DependencyProperty LTProperty =
            DependencyProperty.Register(nameof(LT), typeof(float), typeof(Controller),
                new PropertyMetadata(0f, (d, e) => ((Controller)d).SetFill(((Controller)d).LTShape, (float)e.NewValue)));

        public float RT { get => (float)GetValue(RTProperty); set => SetValue(RTProperty, value); }
        public static readonly DependencyProperty RTProperty =
            DependencyProperty.Register(nameof(RT), typeof(float), typeof(Controller),
                new PropertyMetadata(0f, (d, e) => ((Controller)d).SetFill(((Controller)d).RTShape, (float)e.NewValue)));

        // Center Buttons
        public bool View { get => (bool)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
        public static readonly DependencyProperty ViewProperty =
            DependencyProperty.Register(nameof(View), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnView, (bool)e.NewValue)));

        public bool Menu { get => (bool)GetValue(MenuProperty); set => SetValue(MenuProperty, value); }
        public static readonly DependencyProperty MenuProperty =
            DependencyProperty.Register(nameof(Menu), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnMenu, (bool)e.NewValue)));

        public bool Guide { get => (bool)GetValue(GuideProperty); set => SetValue(GuideProperty, value); }
        public static readonly DependencyProperty GuideProperty =
            DependencyProperty.Register(nameof(Guide), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).BtnGuide, (bool)e.NewValue)));

        // D-Pad (overlays; arrows remain static)
        public bool DpadUp { get => (bool)GetValue(DpadUpProperty); set => SetValue(DpadUpProperty, value); }
        public static readonly DependencyProperty DpadUpProperty =
            DependencyProperty.Register(nameof(DpadUp), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).DpadUpOverlay.FillPath, (bool)e.NewValue)));

        public bool DpadDown { get => (bool)GetValue(DpadDownProperty); set => SetValue(DpadDownProperty, value); }
        public static readonly DependencyProperty DpadDownProperty =
            DependencyProperty.Register(nameof(DpadDown), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).DpadDownOverlay.FillPath, (bool)e.NewValue)));

        public bool DpadLeft { get => (bool)GetValue(DpadLeftProperty); set => SetValue(DpadLeftProperty, value); }
        public static readonly DependencyProperty DpadLeftProperty =
            DependencyProperty.Register(nameof(DpadLeft), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).DpadLeftOverlay.FillPath, (bool)e.NewValue)));

        public bool DpadRight { get => (bool)GetValue(DpadRightProperty); set => SetValue(DpadRightProperty, value); }
        public static readonly DependencyProperty DpadRightProperty =
            DependencyProperty.Register(nameof(DpadRight), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).DpadRightOverlay.FillPath, (bool)e.NewValue)));

        // Sticks (-1..1)
        public short LeftStickX { get => (short)GetValue(LeftStickXProperty); set => SetValue(LeftStickXProperty, value); }
        public static readonly DependencyProperty LeftStickXProperty =
            DependencyProperty.Register(nameof(LeftStickX), typeof(short), typeof(Controller),
                new PropertyMetadata((short)0, (d, e) => ((Controller)d).UpdateLeftStick()));

        public short LeftStickY { get => (short)GetValue(LeftStickYProperty); set => SetValue(LeftStickYProperty, value); }
        public static readonly DependencyProperty LeftStickYProperty =
            DependencyProperty.Register(nameof(LeftStickY), typeof(short), typeof(Controller),
                new PropertyMetadata((short)0, (d, e) => ((Controller)d).UpdateLeftStick()));

        public short RightStickX { get => (short)GetValue(RightStickXProperty); set => SetValue(RightStickXProperty, value); }
        public static readonly DependencyProperty RightStickXProperty =
            DependencyProperty.Register(nameof(RightStickX), typeof(short), typeof(Controller),
                new PropertyMetadata((short)0, (d, e) => ((Controller)d).UpdateRightStick()));

        public short RightStickY { get => (short)GetValue(RightStickYProperty); set => SetValue(RightStickYProperty, value); }
        public static readonly DependencyProperty RightStickYProperty =
            DependencyProperty.Register(nameof(RightStickY), typeof(short), typeof(Controller),
                new PropertyMetadata((short)0, (d, e) => ((Controller)d).UpdateRightStick()));

        public bool LeftStickKnob { get => (bool)GetValue(LStickKnobProperty); set => SetValue(LStickKnobProperty, value); }
        public static readonly DependencyProperty LStickKnobProperty =
            DependencyProperty.Register(nameof(LStickKnob), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).LStickKnob, (bool)e.NewValue)));

        public bool RightStickKnob { get => (bool)GetValue(RStickKnobProperty); set => SetValue(RStickKnobProperty, value); }
        public static readonly DependencyProperty RStickKnobProperty =
            DependencyProperty.Register(nameof(RStickKnob), typeof(bool), typeof(Controller),
                new PropertyMetadata(false, (d, e) => ((Controller)d).SetFill(((Controller)d).RStickKnob, (bool)e.NewValue)));

        public void UpdateAllVisuals()
        {
            ThemeManager.Current.ActualApplicationTheme.ToString();
            SetFill(BtnA_Bottom, A);
            SetFill(BtnB_Right, B);
            SetFill(BtnX_Left, X);
            SetFill(BtnY_Top, Y);
            SetFill(LBShape, LB);
            SetFill(RBShape, RB);
            SetFill(LTShape, LT);
            SetFill(RTShape, RT);
            SetFill(BtnView, View);
            SetFill(BtnMenu, Menu);
            SetFill(BtnGuide, Guide);
            SetFill(DpadUpOverlay.FillPath, DpadUp);
            SetFill(DpadDownOverlay.FillPath, DpadDown);
            SetFill(DpadLeftOverlay.FillPath, DpadLeft);
            SetFill(DpadRightOverlay.FillPath, DpadRight);
            UpdateLeftStick();
            UpdateRightStick();
            SetFill(LStickKnob, LeftStickKnob);
            SetFill(RStickKnob, RightStickKnob);
        }

        void SetFill(Shape shape, bool pressed)
        {
            shape.Fill = pressed ? PressedBrush : ReleasedBrush;
            shape.InvalidateVisual();
        }
        void SetFill(Shape shape, float percentage)
        {
            if (percentage < 0) percentage = 0;
            var alpha = (byte)((1 - percentage) * 255);
            // lerp from PressedBrush to ReleasedBrush
            var pressedColor = PressedBrush.Color;
            var releasedColor = ReleasedBrush.Color;
            var color = Color.FromArgb(255,
                (byte)(releasedColor.R + (pressedColor.R - releasedColor.R) * percentage),
                (byte)(releasedColor.G + (pressedColor.G - releasedColor.G) * percentage),
                (byte)(releasedColor.B + (pressedColor.B - releasedColor.B) * percentage));
            shape.Fill = new SolidColorBrush(color);
            shape.InvalidateVisual();
        }

        void SetOverlay(Shape overlay, bool pressed) =>
            overlay.Visibility = pressed ? Visibility.Visible : Visibility.Collapsed;

        void UpdateLeftStick()
        {
            var max = StickMaxOffset;
            var x = LeftStickX * max / 32767f;
            var y = LeftStickY * max / 32767f;
            if (LStickTranslate != null) { LStickTranslate.X = x; LStickTranslate.Y = y; }
        }

        void UpdateRightStick()
        {
            var max = StickMaxOffset;
            var x = RightStickX * max / 32767f;
            var y = RightStickY * max / 32767f;
            if (RStickTranslate != null) { RStickTranslate.X = x; RStickTranslate.Y = y; }
        }

        static double Magnitude(double x, double y)
        {
            return System.Math.Sqrt(x * x + y * y);
        }
    }
}
