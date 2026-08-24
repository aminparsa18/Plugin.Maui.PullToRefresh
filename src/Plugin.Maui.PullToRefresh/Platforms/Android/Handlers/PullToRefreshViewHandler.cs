using Android.Views;
using AndroidX.Core.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.PullToRefresh;

namespace Plugin.Maui.PullToRefresh.Handlers;

public partial class PullToRefreshViewHandler : ContentViewHandler
{
    private PullInterceptor? _interceptor;

    /// <inheritdoc/>
    protected override ContentViewGroup CreatePlatformView() => base.CreatePlatformView();

    /// <inheritdoc/>
    protected override void ConnectHandler(ContentViewGroup platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ViewAttachedToWindow += OnViewAttached;
    }

    /// <inheritdoc/>
    protected override void DisconnectHandler(ContentViewGroup platformView)
    {
        platformView.ViewAttachedToWindow -= OnViewAttached;
        _interceptor?.Detach();
        base.DisconnectHandler(platformView);
    }

    private void OnViewAttached(object? sender, global::Android.Views.View.ViewAttachedToWindowEventArgs e)
    {
        var view = (ViewGroup)sender!;

        // Post to next layout pass — children exist by then
        view.Post(() =>
        {
            global::Android.Views.View? scrollTarget = FindRecyclerView(view) ?? FindScrollContainer(view);

            if (scrollTarget != null && VirtualView is PullToRefreshView mauiView)
            {
                _interceptor = new PullInterceptor(scrollTarget, mauiView, MauiContext!);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Plugin.Maui.PullToRefresh] Could not find a RecyclerView/ScrollView in the view tree — " +
                    "wrap a CollectionView or ScrollView inside PullToRefreshView.");
                DumpViewTree(view, 0);
            }
        });
    }

    private static void DumpViewTree(global::Android.Views.View view, int depth)
    {
        string indent = new('-', depth * 2);
        System.Diagnostics.Debug.WriteLine($"{indent}{view.GetType().Name} ({view.Id})");
        if (view is ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
                DumpViewTree(vg.GetChildAt(i)!, depth + 1);
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
/// ScrollView (MAUI ScrollView), neither of which this class actually depends on beyond that.
/// </summary>
internal class PullInterceptor : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
{
    private readonly global::Android.Views.View _scrollTarget;
    private readonly PullToRefreshView _mauiView;
    private readonly IMauiContext _mauiContext;

    private float _startY;
    private float _lastY = 0;
    private bool _hasFirstMove = false;
    private bool _isPulling;
    private const float _slopDp = 8f;
    private long _lastUpdateMs = 0;
    private const long _frameMs = 16; // ~60fps

    // The overlay — we hold a reference so we can animate it
    private readonly PullOverlayController _overlay;

    public PullInterceptor(global::Android.Views.View scrollTarget, PullToRefreshView mauiView, IMauiContext context)
    {
        _scrollTarget = scrollTarget;
        _mauiView = mauiView;
        _mauiContext = context;

        _overlay = new PullOverlayController(scrollTarget);
        _scrollTarget.SetOnTouchListener(this);
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

        return false;
    }

    public void Detach()
    {
        _scrollTarget.SetOnTouchListener(null);
        _overlay.Remove();
    }
}
