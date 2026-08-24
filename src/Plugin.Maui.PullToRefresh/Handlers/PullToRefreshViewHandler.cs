using Microsoft.Maui.Handlers;

namespace Plugin.Maui.PullToRefresh.Handlers;

/// <summary>
/// Shared handler declaration. The actual pull-gesture wiring lives in the per-platform partials
/// under <c>Platforms/iOS/Handlers</c> and <c>Platforms/Android/Handlers</c> — MAUI's SingleProject
/// build only compiles the file under the folder matching the current target platform, so this
/// class is safely partial across all of them without <c>#if</c> guards.
/// </summary>
public partial class PullToRefreshViewHandler : ContentViewHandler
{
    /// <summary>Property mapper for <see cref="PullToRefreshView"/>. No custom mappings today — the
    /// handler drives everything through native gesture recognizers, not bindable-property pushes.</summary>
    public static new IPropertyMapper<PullToRefreshView, PullToRefreshViewHandler> Mapper =
        new PropertyMapper<PullToRefreshView, PullToRefreshViewHandler>(ContentViewHandler.Mapper);

    /// <summary>Creates the handler with the default property mapper.</summary>
    public PullToRefreshViewHandler() : base(Mapper) { }
}
