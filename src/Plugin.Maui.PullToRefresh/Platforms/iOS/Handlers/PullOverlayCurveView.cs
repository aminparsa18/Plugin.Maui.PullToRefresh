using CoreAnimation;
using CoreGraphics;
using UIKit;

namespace Plugin.Maui.PullToRefresh.Handlers;

/// <summary>
/// Native UIView that draws the curved colored strip at the top during pull-to-refresh.
/// Mirrors Android's CurvedPullView using a CAShapeLayer.
/// The curve tip follows the finger's X position via xRatio.
/// </summary>
internal class PullOverlayCurveView : UIView
{
    // sqrt() damping constant and the strip's height ceiling — the two knobs most likely to need
    // retuning per device/feel during testing. Both in points. Keep numerically in sync with
    // Android's CurvedPullView (Damping / MaxStripHeightDp).
    private const float Damping = 5.5f;
    private const float MaxStripHeightPt = 80f;

    private readonly CAShapeLayer _shapeLayer = new();
    private float _progress;
    private float _pullPt;
    private float _xRatio = 0.5f;

    public PullOverlayCurveView(UIColor fillColor) : base(CGRect.Empty)
    {
        BackgroundColor = UIColor.Clear;
        UserInteractionEnabled = false;

        _shapeLayer.FillColor = fillColor.CGColor;
        Layer.AddSublayer(_shapeLayer);

        Alpha = 0f;
    }

    public void SetColor(UIColor fillColor) => _shapeLayer.FillColor = fillColor.CGColor;

    public void SetPullProgress(float progress, float pullPt, float xRatio = 0.5f)
    {
        if (_progress == progress && _pullPt == pullPt && _xRatio == xRatio) return;
        _progress = progress;
        _pullPt   = pullPt;
        _xRatio   = xRatio;
        UpdateShape();
    }

    private void UpdateShape()
    {
        // Disable implicit CALayer animation — path must track finger with zero lag
        CATransaction.Begin();
        CATransaction.DisableActions = true;

        float w = (float)Bounds.Width;
        if (w <= 0) w = (float)UIScreen.MainScreen.Bounds.Width;

        // Same damping formula as Android, already in points on iOS
        float dampedPull = (float)Math.Sqrt(_pullPt) * Damping;
        float stripH     = Math.Min(dampedPull, MaxStripHeightPt);

        float yBase = stripH * 0.35f;
        float dip   = stripH * 0.95f;

        // Tip follows finger X, clamped so shape stays valid — mirrors Android
        float center = w * Math.Clamp(_xRatio, 0.15f, 0.85f);

        var path = new UIBezierPath();
        path.MoveTo(new CGPoint(0, 0));
        path.AddLineTo(new CGPoint(w, 0));
        path.AddLineTo(new CGPoint(w, yBase));

        // Right side → center dip (mirrors Android CubicTo block 1)
        path.AddCurveToPoint(
            endPoint:      new CGPoint(center,               yBase + dip),
            controlPoint1: new CGPoint(center + (w * 0.30f), yBase),
            controlPoint2: new CGPoint(center + (w * 0.15f), yBase + dip)
        );

        // Center dip → left side (mirrors Android CubicTo block 2)
        path.AddCurveToPoint(
            endPoint:      new CGPoint(0f,                   yBase),
            controlPoint1: new CGPoint(center - (w * 0.15f), yBase + dip),
            controlPoint2: new CGPoint(center - (w * 0.30f), yBase)
        );

        path.ClosePath();

        _shapeLayer.Path    = path.CGPath;
        _shapeLayer.Opacity = _progress;

        CATransaction.Commit();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        _shapeLayer.Frame = Bounds;
        UpdateShape();
    }
}
