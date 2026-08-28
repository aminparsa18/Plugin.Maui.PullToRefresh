using System.ComponentModel;
using Android.Views;
using AndroidX.Core.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.PullToRefresh.Handlers;

public partial class PullToRefreshViewHandler : ContentViewHandler
{
    private PullInterceptor? _interceptor;

    // ViewAttachedToWindow can fire more than once per Connect/DisconnectHandler pair (e.g. Shell
    // tab switches or a cached nav-stack page being re-shown) without the handler being torn down
    // and rebuilt in between. Guards the two ways that would otherwise create/leak a second
    // interceptor: a live re-attach while one already exists, and a re-attach's posted callback
    // still running the layout pass after DisconnectHandler already ran.
    private bool _isDisconnected;

    /// <inheritdoc/>
    protected override ContentViewGroup CreatePlatformView() => base.CreatePlatformView();

    /// <inheritdoc/>
    protected override void ConnectHandler(ContentViewGroup platformView)
    {
        base.ConnectHandler(platformView);
        _isDisconnected = false;
        platformView.ViewAttachedToWindow += OnViewAttached;
    }

    /// <inheritdoc/>
    protected override void DisconnectHandler(ContentViewGroup platformView)
    {
        _isDisconnected = true;
        platformView.ViewAttachedToWindow -= OnViewAttached;
        _interceptor?.Detach();
        _interceptor = null;
        base.DisconnectHandler(platformView);
    }

    private void OnViewAttached(object? sender, global::Android.Views.View.ViewAttachedToWindowEventArgs e)
    {
        var view = (ViewGroup)sender!;

        // Post to next layout pass — children exist by then
        view.Post(() =>
        {
            // The handler may have been disconnected (or already re-attached) before this ran.
            if (_isDisconnected || VirtualView is not PullToRefreshView mauiView) return;

            InitializeInterceptor(view, mauiView);
        });
    }

    private void InitializeInterceptor(ViewGroup view, PullToRefreshView mauiView)
    {
        // A prior attach cycle may still have an interceptor live — detach it first so it doesn't
        // linger subscribed to mauiView.PropertyChanged with its overlay still in the view tree.
        _interceptor?.Detach();
        _interceptor = null;

        global::Android.Views.View? scrollTarget = FindRecyclerView(view) ?? FindScrollContainer(view);

        if (scrollTarget != null)
        {
            _interceptor = new PullInterceptor(scrollTarget, (ViewGroup)scrollTarget.Parent!, mauiView, MauiContext!);
        }
        else
        {
            // Fallback: no RecyclerView/ScrollView descendant — wrap non-scrollable content
            // (e.g. a Grid) directly. View.CanScrollVertically defaults to false for a plain
            // ViewGroup, so PullInterceptor's "at top" check is naturally always true here —
            // a downward drag from anywhere starts a pull immediately.
            System.Diagnostics.Debug.WriteLine(
                "[Plugin.Maui.PullToRefresh] No RecyclerView/ScrollView found — using the " +
                "non-scrollable fallback (pull tracks immediately).");
            _interceptor = new PullInterceptor(view, view, mauiView, MauiContext!);
        }
    }

    private static RecyclerView? FindRecyclerView(ViewGroup root)
    {
        for (int i = 0; i < root.ChildCount; i++)
        {
            global::Android.Views.View? child = root.GetChildAt(i);
            if (child is RecyclerView rv) return rv;
            if (child is ViewGroup vg)
            {
                RecyclerView? found = FindRecyclerView(vg);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Fallback for pages whose scrollable content is a MAUI ScrollView (renders as a
    /// NestedScrollView/ScrollView on Android) rather than a CollectionView. Only consulted when
    /// <see cref="FindRecyclerView"/> finds nothing, so CollectionView-based usages are unaffected.
    /// </summary>
    private static global::Android.Views.View? FindScrollContainer(ViewGroup root)
    {
        for (int i = 0; i < root.ChildCount; i++)
        {
            global::Android.Views.View? child = root.GetChildAt(i);
            if (child is NestedScrollView or global::Android.Widget.ScrollView) return child;
            if (child is ViewGroup vg)
            {
                global::Android.Views.View? found = FindScrollContainer(vg);
                if (found != null) return found;
            }
        }
        return null;
    }
}

/// <summary>
/// Wraps the scroll target's touch listener so we can detect downward pulls at top. Works with any
/// View that supports CanScrollVertically — a RecyclerView (CollectionView) or a NestedScrollView/
/// ScrollView (MAUI ScrollView), neither of which this class actually depends on beyond that. Also
/// used, with the content's own root view as the scroll target, as the non-scrollable fallback —
/// <c>CanScrollVertically</c> defaults to false there, so "at top" is always true.
/// </summary>
internal class PullInterceptor : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
{
    private readonly global::Android.Views.View _scrollTarget;
    private readonly PullToRefreshView _mauiView;
    private readonly IMauiContext _mauiContext;

    // True when _scrollTarget is the content's own root (the non-scrollable fallback) rather than
    // a real RecyclerView/ScrollView. A plain ViewGroup doesn't consume ACTION_DOWN on its own, so
    // if we don't claim it ourselves (return true), Android never establishes us as the touch
    // target and silently stops delivering MOVE/UP for the gesture to this listener. A real
    // RecyclerView/ScrollView already claims DOWN itself via its own onTouchEvent, so this doesn't
    // need to (and shouldn't, to avoid swallowing its native scroll/fling handling) apply there.
    private readonly bool _claimsTouch;

    private float _startY;
    private float _lastY = 0;
    private bool _hasFirstMove = false;
    private bool _isPulling;
    private const float _slopDp = 8f;
    private long _lastUpdateMs = 0;
    private const long _frameMs = 16; // ~60fps

    // The overlay — we hold a reference so we can animate it
    private readonly PullOverlayController _overlay;

    public PullInterceptor(global::Android.Views.View scrollTarget, ViewGroup overlayContainer, PullToRefreshView mauiView, IMauiContext context)
    {
        _scrollTarget = scrollTarget;
        _mauiView = mauiView;
        _mauiContext = context;
        _claimsTouch = ReferenceEquals(scrollTarget, overlayContainer);

        _overlay = new PullOverlayController(overlayContainer, mauiView.StripColor);
        _scrollTarget.SetOnTouchListener(this);

        _mauiView.PropertyChanged += OnMauiViewPropertyChanged;
    }

    private void OnMauiViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PullToRefreshView.StripColor))
            _overlay.SetColor(_mauiView.StripColor);
    }

    public bool OnTouch(global::Android.Views.View? v, MotionEvent? e)
    {
        if (e == null) return false;

        float density = _scrollTarget.Context!.Resources!.DisplayMetrics!.Density;
        float slopPx = _slopDp * density;

        switch (e.Action)
        {
            case MotionEventActions.Down:
                _lastY = e.RawY;
                _hasFirstMove = false;
                _isPulling = false;
                break;

            case MotionEventActions.Move:
                if (!_hasFirstMove)
                {
                    _lastY = e.RawY;
                    _hasFirstMove = true;
                    break;
                }

                float deltaFromLast = e.RawY - _lastY;
                bool atTop = !_scrollTarget.CanScrollVertically(-1);

                // Detect pull start
                if (!_isPulling && atTop && deltaFromLast > slopPx)
                {
                    _startY = _lastY;
                    _isPulling = true;
                }

                // Once pulling, update on EVERY throttled frame regardless of direction
                if (_isPulling)
                {
                    long now = Java.Lang.JavaSystem.CurrentTimeMillis();
                    if (now - _lastUpdateMs >= _frameMs)
                    {
                        _lastUpdateMs = now;

                        // Y: distance from pull start — clamp to 0 so it shrinks back naturally
                        float totalDp = Math.Max(0f, (e.RawY - _startY) / density);

                        // X: live finger position
                        float xRatio = Math.Clamp(e.RawX / _scrollTarget.Width, 0f, 1f);

                        _overlay.UpdatePull(totalDp, xRatio);

                        // If finger pulled back to start, cancel the pull state
                        if (totalDp <= 0f)
                        {
                            _isPulling = false;
                            _overlay.AnimateReset();
                        }
                    }
                }

                _lastY = e.RawY;
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _hasFirstMove = false;
                if (_isPulling)
                {
                    float totalDp = (e.RawY - _startY) / density;
                    _isPulling = false;

                    if (totalDp >= PullOverlayController.TriggerThreshold)
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
                }
                break;
        }

        // Claim the gesture in the non-scrollable fallback so Android keeps routing MOVE/UP to us
        // (see _claimsTouch); otherwise decline, so the real RecyclerView/ScrollView still scrolls.
        return _claimsTouch;
    }

    public void Detach()
    {
        _mauiView.PropertyChanged -= OnMauiViewPropertyChanged;
        _scrollTarget.SetOnTouchListener(null);
        _overlay.Remove();
    }
}
