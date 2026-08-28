using Plugin.Maui.PullToRefresh.Handlers;

namespace Plugin.Maui.PullToRefresh;

/// <summary>
/// Extension methods for registering Plugin.Maui.PullToRefresh with a <see cref="MauiAppBuilder"/>.
/// </summary>
public static class AppHostBuilderExtensions
{
    /// <summary>
    /// Registers the <see cref="PullToRefreshView"/> handler so <c>PullToRefreshView</c> renders
    /// with its native platform implementation. Call this from <c>MauiProgram.CreateMauiApp</c>
    /// instead of manually calling <c>ConfigureMauiHandlers</c>.
    /// </summary>
    /// <param name="builder">The app builder to configure.</param>
    /// <returns>The same <see cref="MauiAppBuilder"/> instance, for chaining.</returns>
    public static MauiAppBuilder UsePullToRefresh(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<PullToRefreshView, PullToRefreshViewHandler>();
        });

        return builder;
    }
}
