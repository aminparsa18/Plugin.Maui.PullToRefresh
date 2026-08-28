using System.Windows.Input;

namespace Plugin.Maui.PullToRefresh;

/// <summary>
/// A drop-in replacement for wrapping scrollable content (typically a <c>CollectionView</c> or
/// <c>ScrollView</c>) with pull-to-refresh behavior. Unlike MAUI's built-in <c>RefreshView</c>,
/// there is no spinner: the platform handler draws a native curved strip that stretches with the
/// finger and reports back through <see cref="Command"/> / <see cref="Refreshing"/> once the pull
/// clears the trigger threshold.
/// </summary>
public class PullToRefreshView : ContentView
{
    /// <summary>Backing store for <see cref="Command"/>.</summary>
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(PullToRefreshView));

    /// <summary>Executed when the user pulls past the trigger threshold and releases.</summary>
    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Backing store for <see cref="IsRefreshing"/>.</summary>
    public static readonly BindableProperty IsRefreshingProperty =
        BindableProperty.Create(nameof(IsRefreshing), typeof(bool), typeof(PullToRefreshView), false);

    /// <summary>
    /// Reserved for host pages that want to track refresh state explicitly. The handler itself
    /// does not read or write this property — it fires <see cref="Refreshing"/> / <see cref="Command"/>
    /// and lets the host decide when the operation is done.
    /// </summary>
    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    /// <summary>Backing store for <see cref="StripColor"/>.</summary>
    public static readonly BindableProperty StripColorProperty =
        BindableProperty.Create(nameof(StripColor), typeof(Color), typeof(PullToRefreshView), Color.FromArgb("#CC5B80C1"));

    /// <summary>
    /// Color of the curved strip drawn during a pull. The color's own alpha channel sets the
    /// strip's opacity at full pull — it fades in with pull progress from there. Defaults to the
    /// plugin's brand blue (<c>#CC5B80C1</c>, ~80% opaque).
    /// </summary>
    public Color StripColor
    {
        get => (Color)GetValue(StripColorProperty);
        set => SetValue(StripColorProperty, value);
    }

    /// <summary>Fired when the user pulls far enough — host pages can bind <see cref="Command"/> instead if preferred.</summary>
    public event EventHandler? Refreshing;

    /// <summary>Called by the platform handler once a pull clears the trigger threshold.</summary>
    internal void NotifyRefreshing() => Refreshing?.Invoke(this, EventArgs.Empty);
}
