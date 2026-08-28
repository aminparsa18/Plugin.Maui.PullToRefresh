# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET MAUI library (`Plugin.Maui.PullToRefresh`) implementing a pull-to-refresh
control with a native, elastic curved-strip animation — not a spinner, not a
wrapper around `RefreshView`. There is no shared native rendering code: iOS and
Android each have an independent implementation of the same math and gesture
state machine, unified only by the shared MAUI-facing `PullToRefreshView`
control and `PullToRefreshViewHandler` partial class.

There is no `.sln` file. Work directly with the two `.csproj` files:
- `src/Plugin.Maui.PullToRefresh/Plugin.Maui.PullToRefresh.csproj` — the library
- `example/Plugin.Maui.PullToRefresh.Example/Plugin.Maui.PullToRefresh.Example.csproj` — a runnable demo app that references the library via `ProjectReference`

Both target `net10.0-android` and `net10.0-ios` only — Mac Catalyst and
Windows are not implemented (see the "Platform support" table in README.md).
There are no automated tests in this repo; verification is done by building
and running the example app on a device/simulator.

## Commands

Build the library:
```bash
dotnet build src/Plugin.Maui.PullToRefresh/Plugin.Maui.PullToRefresh.csproj -f net10.0-android   # or -f net10.0-ios
```

Build/run the example app:
```bash
cd example/Plugin.Maui.PullToRefresh.Example
dotnet build -f net10.0-android   # or -f net10.0-ios
```

Deploy the example to a connected Android device (from repo root):
```bash
scripts/run-android-device.sh <device-serial>            # one-shot build + deploy
scripts/run-android-device.sh --watch <device-serial>     # dotnet watch: rebuild/redeploy on change, streams logs
```
Find `<device-serial>` via `adb devices`.

Pack the NuGet package (mirrors what CI does):
```bash
dotnet pack src/Plugin.Maui.PullToRefresh/Plugin.Maui.PullToRefresh.csproj -c Release -o ./nupkg
```

Publishing to nuget.org (`.github/workflows/publish.yml`) runs on GitHub
release publish or manual dispatch, using NuGet Trusted Publishing (OIDC) —
there is no stored API key. Bump `<Version>` in the library's `.csproj` before
cutting a release.

## Architecture

**Gesture detection happens at the platform layer**, not through a MAUI
`PanGestureRecognizer`: a `UIPanGestureRecognizer` on iOS
(`PullGestureController` in `Platforms/iOS/Handlers/PullToRefreshViewHandler.cs`),
a raw `IOnTouchListener` on Android (`PullInterceptor` in
`Platforms/Android/Handlers/PullToRefreshViewHandler.cs`). Both look for "at
the top of the scrollable content and moving down past an 8dp/pt slop" before
treating the touch as a pull, so normal scrolling is never intercepted. Both
throttle updates to ~60fps (`_frameMs = 16`) independent of native event rate.

**The scrollable content is auto-discovered** by walking the native view tree
under whatever is placed inside `PullToRefreshView`:
- iOS: `FindScrollView` looks for a `UIScrollView` descendant.
- Android: `FindRecyclerView` looks for a `RecyclerView` first (covers
  `CollectionView`), then `FindScrollContainer` looks for a
  `NestedScrollView`/`ScrollView` (covers MAUI `ScrollView`).

**Non-scrollable content falls back gracefully.** If nothing scrollable is
found (e.g. a plain `Grid`), the handler wraps the content's own root view
instead and treats "at top" as always true, so a downward drag from anywhere
starts a pull immediately. On Android this requires the touch listener to
*claim* the gesture (`_claimsTouch`, return `true` from `OnTouch`) only in
this fallback case — a real `RecyclerView`/`ScrollView` already claims
`ACTION_DOWN` itself, so claiming it there would swallow native scroll/fling.

**Each platform pairs a gesture controller with an overlay controller:**
- `PullGestureController`/`PullInterceptor` — owns the gesture state machine
  (slop detection, pull tracking, trigger/reset decision) and fires
  `Command`/`Refreshing` on the MAUI view when the pull crosses
  `PullOverlayController.TriggerThreshold` (72dp/pt) and the finger lifts.
- `PullOverlayController` — owns the overlay view lifecycle (add/remove from
  the view tree, alpha fade, reset/trigger animations) and delegates the
  actual curve drawing to a platform view (`PullOverlayCurveView` on iOS
  using `CAShapeLayer`; the Android equivalent draws directly via
  `Canvas`/`Path` — no `GraphicsView` on either platform).

Both controllers compute strip height as `min(sqrt(pullDistance) * 5.5,
maxHeight)` (damped, not linear — gives the elastic feel) and the curve tip's
X position tracks the finger's live horizontal position, clamped to the
middle 60% of the width so Bézier control points never invert. Keep the two
platform implementations numerically in sync when tuning this math — there's
no shared code to change once and get both for free.

**Registration**: `AppHostBuilderExtensions.UsePullToRefresh()` is the
public entry point apps call from `MauiProgram.CreateMauiApp()`; it wraps
`ConfigureMauiHandlers` to register `PullToRefreshViewHandler` for
`PullToRefreshView`. The handler's `Mapper` (in `Handlers/PullToRefreshViewHandler.cs`)
is intentionally empty — the handler drives everything through native gesture
recognizers rather than bindable-property pushes; `StripColor` changes are
instead picked up via `PropertyChanged` subscriptions inside each platform's
gesture controller.

**Public API surface** (`PullToRefreshView.cs`) is deliberately small:
`Command`, `IsRefreshing` (host-managed only — never read/written by the
handler), `StripColor` (alpha channel sets max strip opacity), and a
`Refreshing` event as an alternative to `Command` binding.
