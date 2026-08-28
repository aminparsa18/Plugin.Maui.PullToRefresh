using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Animations;
using Microsoft.Maui.Platform;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;

namespace Plugin.Maui.PullToRefresh.Handlers;

/// <summary>
/// Adds a curved colored strip as a sibling view within the given container — normally the scroll
/// target's parent, or the content's own root view for the non-scrollable fallback. No MAUI
/// GraphicsView involved — pure native for reliable touch + rendering.
/// </summary>
internal class PullOverlayController
{
    public const float TriggerThreshold = 72f; // dp

    private readonly CurvedPullView _overlayView;
    private readonly ViewGroup _parent;
    private bool _attached;

    // Last real pull state from UpdatePull, frozen and reused by AnimateReset/AnimateTrigger so
    // those animations only fade the strip out rather than reconstructing a fake pull distance
    // from the alpha value (which used to make the geometry shrink in lockstep with the fade).
    private float _lastPullDp;
    private float _lastXRatio = 0.5f;

    // Tracks the in-flight reset/trigger animation so Remove() can stop it instead of leaving it
    // running (and, for AnimateTrigger, still able to fire its onComplete/refresh callback) after
    // the overlay has been torn down.
    private ValueAnimator? _animation;
    private bool _animationCanceled;

    public PullOverlayController(ViewGroup container, Microsoft.Maui.Graphics.Color stripColor)
    {
        _parent = container;

        _overlayView = new CurvedPullView(container.Context!, stripColor.ToPlatform());

        // Use explicit pixel size from the parent — NOT MatchParent
        var lp = new ViewGroup.LayoutParams(_parent.Width, _parent.Height);
        _parent.AddView(_overlayView, lp);
        _attached = true;

        // Force measure + layout immediately — this is why it was 0x0
        _overlayView.Measure(
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(_parent.Width, MeasureSpecMode.Exactly),
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(_parent.Height, MeasureSpecMode.Exactly)
        );
        _overlayView.Layout(0, 0, _parent.Width, _parent.Height);

        // Bring to front so it renders above RecyclerView
        _overlayView.BringToFront();
        _parent.Invalidate();

        _overlayView.Alpha = 0f;
        _overlayView.SetPullProgress(1f, 100f);
    }

    public void SetColor(Microsoft.Maui.Graphics.Color stripColor) => _overlayView.SetColor(stripColor.ToPlatform());

    public void UpdatePull(float dp, float xRatio = 0.5f)
    {
        _lastPullDp = dp;
        _lastXRatio = xRatio;

        float alpha = Math.Clamp(dp / 30f, 0f, 1f);
        float progress = Math.Clamp(dp / TriggerThreshold, 0f, 1f);

        // Ensure we're on main thread for view updates
        _overlayView.Post(() =>
        {
            _overlayView.Alpha = alpha;
            _overlayView.SetPullProgress(progress, dp, xRatio);
        });
    }

    public void AnimateReset()
    {
        CancelAnimation();

        float startProgress = _overlayView.Alpha;
        float pullDp = _lastPullDp;
        float xRatio = _lastXRatio;

        ValueAnimator anim = ValueAnimator.OfFloat(startProgress, 0f)!;
        anim.SetDuration(200);
        anim.SetInterpolator(new DecelerateInterpolator(2f));
        anim.Update += (_, e) =>
        {
            float val = (float)e.Animation!.AnimatedValue!;
            _overlayView.Alpha = val;
            // Geometry frozen at the last real pull state — only alpha and the paint-alpha
            // fraction (val) animate, so the strip fades out in place instead of shrinking
            // toward a synthetic pull distance reconstructed from the alpha value.
            _overlayView.SetPullProgress(val, pullDp, xRatio);
        };
        anim.AnimationEnd += (_, _) => _animation = null;
        _animation = anim;
        anim.Start();
    }

    public void AnimateTrigger(Action onComplete)
    {
        CancelAnimation();

        float pullDp = _lastPullDp;
        float xRatio = _lastXRatio;

        // Quick pulse then fade out
        ValueAnimator anim = ValueAnimator.OfFloat(1f, 0f)!;
        anim.SetDuration(250);
        anim.SetInterpolator(new AccelerateInterpolator());
        anim.Update += (_, e) =>
        {
            float val = (float)e.Animation!.AnimatedValue!;
            _overlayView.Alpha = val;
            // See AnimateReset — geometry stays frozen, only the fade animates.
            _overlayView.SetPullProgress(val, pullDp, xRatio);
        };
        anim.AnimationEnd += (_, _) =>
        {
            _animation = null;
            // Android's ValueAnimator.Cancel() also raises AnimationEnd (documented behavior,
            // not a binding quirk) — skip firing the refresh command when this end came from
            // CancelAnimation() tearing the overlay down, not from the animation completing.
            if (!_animationCanceled)
                onComplete();
        };
        _animation = anim;
        anim.Start();
    }

    private void CancelAnimation()
    {
        // Capture locally — Cancel() below synchronously raises AnimationEnd, whose handler sets
        // the _animation field to null, so the field itself can't be trusted after calling it.
        ValueAnimator? anim = _animation;
        if (anim == null) return;

        _animationCanceled = true;
        anim.Cancel();
        // Explicitly detach listeners rather than relying on Dispose() to sever them — the
        // native peer's listener list is what's holding the C# closures (which reach _overlayView
        // and, for a trigger animation, onComplete/_mauiView) alive on the Java side.
        anim.RemoveAllUpdateListeners();
        anim.RemoveAllListeners();
        anim.Dispose();
        _animation = null;
        _animationCanceled = false;
    }

    public void Remove()
    {
        CancelAnimation();

        if (_attached)
        {
            _parent.RemoveView(_overlayView);
            _attached = false;
        }
    }
}

/// <summary>
/// The actual native view that draws the curved shape.
/// </summary>
internal class CurvedPullView : global::Android.Views.View
{
    // sqrt() damping constant and the strip's height ceiling — the two knobs most likely to need
    // retuning per device/feel during testing. Both in dp.
    private const float Damping = 5.5f;
    private const float MaxStripHeightDp = 80f;

    private float _progress;
    private float _pullDp;
    private float _xRatio = 0.5f;

    private readonly Paint _fillPaint;
    private readonly Path _path = new();
    private byte _maxAlpha; // strip's alpha ceiling, taken from the color's own alpha channel

    // Pre-computed control points — updated only when values change
    private float _lastW, _lastStripH, _lastCurveDepth, _lastCenterX;

    public CurvedPullView(Context context, Color color) : base(context)
    {
        _fillPaint = new Paint(PaintFlags.AntiAlias);
        _fillPaint.SetStyle(Paint.Style.Fill);
        SetColor(color);

        // Hardware layer = GPU caches this view, alpha changes are free
        SetLayerType(LayerType.Hardware, null);
    }

    public void SetColor(Color color)
    {
        _maxAlpha = color.A;
        _fillPaint.Color = color;
        PostInvalidate();
    }

    public void SetPullProgress(float progress, float pullDp, float xRatio = 0.5f)
    {
        if (_progress == progress && _pullDp == pullDp && _xRatio == xRatio) return; // no-op if nothing changed
        _progress = progress;
        _pullDp = pullDp;
        _xRatio = xRatio;
        PostInvalidate();
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas == null || _progress <= 0f) return;

        float w = Width;

        float dampedPull = (float)Math.Sqrt(_pullDp) * Damping;
        float density = Resources!.DisplayMetrics!.Density;
        float stripH = Math.Min(dampedPull * density, MaxStripHeightDp * density);

        float yBase = stripH * 0.35f;

        // how deep the middle "hill" goes down (tweak 0.8f..1.3f)
        float dip = stripH * 0.95f;

        // Tip follows finger, clamped so shape stays valid
        float center = w * Math.Clamp(_xRatio, 0.15f, 0.85f);

        // Only rebuild path if dimensions actually changed
        if (w != _lastW || stripH != _lastStripH || dip != _lastCurveDepth || center != _lastCenterX)
        {
            _lastW = w;
            _lastStripH = stripH;
            _lastCurveDepth = dip;
            _lastCenterX = center;

            _path.Reset();
            _path.MoveTo(0, 0);
            _path.LineTo(w, 0);
            _path.LineTo(w, yBase);

            // Original shape preserved — just swap w*0.50 for center, others offset from it
            _path.CubicTo(
                center + (w * 0.30f), yBase,        // was w * 0.80f
                center + (w * 0.15f), yBase + dip,  // was w * 0.65f
                center, yBase + dip   // was w * 0.50f — TIP follows finger
            );
            _path.CubicTo(
                center - (w * 0.15f), yBase + dip,  // was w * 0.35f
                center - (w * 0.30f), yBase,        // was w * 0.20f
                0f, yBase
            );

            _path.Close();
        }

        _fillPaint.Alpha = (int)(_maxAlpha * _progress);
        canvas.DrawPath(_path, _fillPaint);
    }
}
