using System.ComponentModel;
using CoreAnimation;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.PullToRefresh;
using UIKit;
using ContentView = Microsoft.Maui.Platform.ContentView;

namespace Plugin.Maui.PullToRefresh.Handlers;

public partial class PullToRefreshViewHandler : ContentViewHandler
{
    private PullGestureController? _gestureController;

    /// <inheritdoc/>
    protected override ContentView CreatePlatformView() => base.CreatePlatformView();

    /// <inheritdoc/>
    protected override void ConnectHandler(ContentView platformView)
    {
        base.ConnectHandler(platformView);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (VirtualView is not PullToRefreshView mauiView) return;

            UIScrollView? scrollView = FindScrollView(platformView);

            if (scrollView != null)
            {
                UIView container = scrollView.Superview ?? scrollView;
                _gestureController = new PullGestureController(scrollView, scrollView, container, mauiView);
            }
            else
            {
                // Fallback: no UIScrollView descendant — wrap non-scrollable content (e.g. a Grid)
                // directly. Passing null for scrollView makes PullGestureController treat "at top"
                // as always true, so a downward drag from anywhere starts a pull immediately.
                System.Diagnostics.Debug.WriteLine(
                    "[Plugin.Maui.PullToRefresh] No UIScrollView found — using the non-scrollable " +
                    "fallback (pull tracks immediately).");
                _gestureController = new PullGestureController(platformView, null, platformView, mauiView);
            }
        });
    }

    /// <inheritdoc/>
    protected override void DisconnectHandler(ContentView platformView)
    {
        _gestureController?.Detach();
        _gestureController = null;
        base.DisconnectHandler(platformView);
    }

    private static UIScrollView? FindScrollView(UIView root)
    {
        foreach (UIView child in root.Subviews)
        {
            if (child is UIScrollView sv) return sv;
            UIScrollView? found = FindScrollView(child);
            if (found != null) return found;
        }
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Overlay Controller — mirrors Android PullOverlayController
// ─────────────────────────────────────────────────────────────────────────────

internal class PullOverlayController
{
    public const float TriggerThreshold = 72f; // points (density-independent on iOS)

    private readonly PullOverlayCurveView _overlayView;
    private readonly UIView _parent;

    /// <param name="container">
    /// View the overlay is added into as a sibling — normally the scroll view's superview, or the
    /// content's own root view for the non-scrollable fallback.
    /// </param>
    /// <param name="stripColor">Initial color of the curved strip.</param>
    public PullOverlayController(UIView container, UIColor stripColor)
    {
        _parent = container;

        _overlayView = new PullOverlayCurveView(stripColor)
        {
            Frame = _parent.Bounds,
            AutoresizingMask =
                UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };

        _parent.AddSubview(_overlayView);
        _parent.BringSubviewToFront(_overlayView);

        // Pre-warm path, then hide
        _overlayView.SetPullProgress(1f, 100f, 0.5f);
        _overlayView.Alpha = 0f;
    }

    public void SetColor(UIColor stripColor) => _overlayView.SetColor(stripColor);

    /// <param name="pt">Pull distance in points from start Y</param>
    /// <param name="xRatio">Finger X as 0..1 fraction of scroll view width</param>
    public void UpdatePull(float pt, float xRatio = 0.5f)
    {
        float alpha = Math.Clamp(pt / 30f, 0f, 1f);
        float progress = Math.Clamp(pt / TriggerThreshold, 0f, 1f);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _overlayView.Alpha = alpha;
            _overlayView.SetPullProgress(progress, pt, xRatio);
        });
    }

    public void AnimateReset() => AnimateFadeOut(duration: 0.2, easing: CAMediaTimingFunction.EaseOut, onComplete: null);

    public void AnimateTrigger(Action onComplete) => AnimateFadeOut(duration: 0.25, easing: CAMediaTimingFunction.EaseIn, onComplete: onComplete);

    private void AnimateFadeOut(double duration, NSString easing, Action? onComplete)
    {
        CATransaction.Begin();
        CATransaction.AnimationDuration = duration;
        CATransaction.SetValueForKey(
            CAMediaTimingFunction.FromName(easing),
            (NSString)"animationTimingFunction"
        );
        CATransaction.CompletionBlock = () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _overlayView.Alpha = 0f;
                _overlayView.SetPullProgress(0f, 0f, 0.5f);
                onComplete?.Invoke();
            });
        };

        _overlayView.Layer.Opacity = 0f;

        CATransaction.Commit();
    }

    public void Remove() => _overlayView.RemoveFromSuperview();
}


// ─────────────────────────────────────────────────────────────────────────────
// Gesture Controller — mirrors Android PullInterceptor
// ─────────────────────────────────────────────────────────────────────────────

internal class PullGestureController
{
    private readonly UIView _target;
    private readonly UIScrollView? _scrollView; // null for the non-scrollable fallback
    private readonly UIView _container;
    private readonly PullToRefreshView _mauiView;
    private readonly PullOverlayController _overlay;
    private readonly UIPanGestureRecognizer _pan;

    private float _startY;
    private bool _hasFirstMove;
    private bool _isPulling;

    private const float _slopPt  = 8f;
    private long  _lastUpdateMs = 0;
    private const long _frameMs  = 16; // ~60 fps

    /// <param name="target">View the pan gesture recognizer is attached to.</param>
    /// <param name="scrollView">
    /// The scrollable view, used only for the "at top" check — pass null for the non-scrollable
    /// fallback, which treats "at top" as always true.
    /// </param>
    /// <param name="overlayContainer">View the curve overlay is drawn into, sized to its bounds.</param>
    /// <param name="mauiView">The MAUI-facing control, for reading StripColor and firing Command/Refreshing.</param>
    public PullGestureController(UIView target, UIScrollView? scrollView, UIView overlayContainer, PullToRefreshView mauiView)
    {
        _target     = target;
        _scrollView = scrollView;
        _container  = overlayContainer;
        _mauiView   = mauiView;
        _overlay    = new PullOverlayController(overlayContainer, mauiView.StripColor.ToPlatform());

        _pan = new UIPanGestureRecognizer(OnPan)
        {
            ShouldRecognizeSimultaneously = (_, _) => true // don't block scroll
        };
        _target.AddGestureRecognizer(_pan);

        _mauiView.PropertyChanged += OnMauiViewPropertyChanged;
    }

    private void OnMauiViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PullToRefreshView.StripColor))
            _overlay.SetColor(_mauiView.StripColor.ToPlatform());
    }

    private void OnPan(UIPanGestureRecognizer recognizer)
    {
        UIView container = _container;
        bool atTop = _scrollView is null || _scrollView.ContentOffset.Y <= 0;

        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                _hasFirstMove = false;
                _isPulling    = false;
                break;

            case UIGestureRecognizerState.Changed:
            {
                float currentY = (float)recognizer.TranslationInView(container).Y;

                // Skip the very first move event — mirrors Android _hasFirstMove pattern
                if (!_hasFirstMove)
                {
                    _hasFirstMove = true;
                    _startY = currentY;
                    break;
                }

                // Detect pull start: at top + moving down past slop
                if (!_isPulling && atTop && (currentY - _startY) > _slopPt)
                {
                    _startY    = currentY; // anchor exactly where slop was crossed
                    _isPulling = true;
                }

                // Once pulling, update every throttled frame regardless of direction
                if (_isPulling)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now - _lastUpdateMs >= _frameMs)
                    {
                        _lastUpdateMs = now;

                        // Clamp to 0 — shrinks back naturally when finger reverses
                        float totalPt = Math.Max(0f, currentY - _startY);

                        // X: live finger position as ratio — mirrors Android e.RawX / recycler.Width
                        float xRatio = Math.Clamp(
                            (float)recognizer.LocationInView(container).X / (float)container.Bounds.Width,
                            0f, 1f
                        );

                        _overlay.UpdatePull(totalPt, xRatio);

                        // Cancel pull state if finger pulled all the way back — mirrors Android
                        if (totalPt <= 0f)
                        {
                            _isPulling = false;
                            _overlay.AnimateReset();
                        }
                    }
                }
                break;
            }

            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            {
                _hasFirstMove = false;
                if (!_isPulling) break;

                float totalPt = Math.Max(
                    0f,
                    (float)recognizer.TranslationInView(container).Y - _startY
                );
                _isPulling = false;

                if (totalPt >= PullOverlayController.TriggerThreshold)
                {
                    _overlay.AnimateTrigger(() =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _mauiView.Command?.Execute(null);
                            _mauiView.NotifyRefreshing();
                        });
                    });
                }
                else
                {
                    _overlay.AnimateReset();
                }
                break;
            }
        }
    }

    public void Detach()
    {
        _mauiView.PropertyChanged -= OnMauiViewPropertyChanged;
        _target.RemoveGestureRecognizer(_pan);
        _overlay.Remove();
    }
}
