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
    private readonly UIBezierPath _path = new();
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

        // Geometry (the curve path) depends only on pullPt/xRatio/Bounds.Width — progress only
        // drives opacity. Skip rebuilding the Bézier path when just the opacity is changing.
        bool geometryChanged = _pullPt != pullPt || _xRatio != xRatio;

        _progress = progress;
        _pullPt   = pullPt;
        _xRatio   = xRatio;
        UpdateShape(geometryChanged);
    }

    private void UpdateShape(bool rebuildGeometry = true)
    {
        // Bounds isn't laid out yet (e.g. the constructor's pre-warm call fires before the first
        // layout pass) — skip this draw. LayoutSubviews() re-invokes UpdateShape() once real
        // bounds land, so nothing is lost; a stale/empty path for one frame beats guessing a width
        // (screen width is wrong under Split View/Stage Manager/rotation).
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Disable implicit CALayer animation — path and opacity must track the finger with zero
        // lag, so both writes below always happen inside this transaction, even when the path
        // rebuild itself is skipped.
        CATransaction.Begin();
        CATransaction.DisableActions = true;

        if (rebuildGeometry)
        {
            float w = (float)Bounds.Width;

            // Same damping formula as Android, already in points on iOS
            float dampedPull = (float)Math.Sqrt(_pullPt) * Damping;
            float stripH     = Math.Min(dampedPull, MaxStripHeightPt);

            float yBase = stripH * 0.35f;
            float dip   = stripH * 0.95f;

            // Tip follows finger X, clamped so shape stays valid — mirrors Android
            float center = w * Math.Clamp(_xRatio, 0.15f, 0.85f);

            _path.RemoveAllPoints();
            _path.MoveTo(new CGPoint(0, 0));
            _path.AddLineTo(new CGPoint(w, 0));
            _path.AddLineTo(new CGPoint(w, yBase));

            // Right side → center dip (mirrors Android CubicTo block 1)
            _path.AddCurveToPoint(
                endPoint:      new CGPoint(center,               yBase + dip),
                controlPoint1: new CGPoint(center + (w * 0.30f), yBase),
                controlPoint2: new CGPoint(center + (w * 0.15f), yBase + dip)
            );

            // Center dip → left side (mirrors Android CubicTo block 2)
            _path.AddCurveToPoint(
                endPoint:      new CGPoint(0f,                   yBase),
                controlPoint1: new CGPoint(center - (w * 0.15f), yBase + dip),
                controlPoint2: new CGPoint(center - (w * 0.30f), yBase)
            );

            _path.ClosePath();

            _shapeLayer.Path = _path.CGPath;
        }

        _shapeLayer.Opacity = _progress;

        CATransaction.Commit();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        _shapeLayer.Frame = Bounds;
        UpdateShape(rebuildGeometry: true);
    }
}
