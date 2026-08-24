using CoreAnimation;
using Foundation;
using Microsoft.Maui.Handlers;
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
            UIScrollView? scrollView = FindScrollView(platformView);

            if (scrollView != null && VirtualView is PullToRefreshView mauiView)
            {
                _gestureController = new PullGestureController(scrollView, mauiView);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Plugin.Maui.PullToRefresh] Could not find a UIScrollView in the view tree — " +
                    "wrap a CollectionView or ScrollView inside PullToRefreshView.");
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

    public PullOverlayController(UIView scrollView)
    {
        _parent = scrollView.Superview ?? scrollView;

        _overlayView = new PullOverlayCurveView
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
    private readonly UIScrollView _scrollView;
    private readonly PullToRefreshView _mauiView;
    private readonly PullOverlayController _overlay;
    private readonly UIPanGestureRecognizer _pan;

    private float _startY;
    private bool _hasFirstMove;
    private bool _isPulling;

    private const float _slopPt  = 8f;
    private long  _lastUpdateMs = 0;
    private const long _frameMs  = 16; // ~60 fps

    public PullGestureController(UIScrollView scrollView, PullToRefreshView mauiView)
    {
        _scrollView = scrollView;
        _mauiView   = mauiView;
        _overlay    = new PullOverlayController(scrollView);

        _pan = new UIPanGestureRecognizer(OnPan)
        {
            ShouldRecognizeSimultaneously = (_, _) => true // don't block scroll
        };
        _scrollView.AddGestureRecognizer(_pan);
    }

    private void OnPan(UIPanGestureRecognizer recognizer)
    {
        UIView? container = _scrollView.Superview ?? _scrollView;
        bool atTop = _scrollView.ContentOffset.Y <= 0;

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
        _scrollView.RemoveGestureRecognizer(_pan);
        _overlay.Remove();
    }
}
