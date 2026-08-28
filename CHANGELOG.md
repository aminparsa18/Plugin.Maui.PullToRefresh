# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.3] - 2026-08-28

### Changed
- `PullToRefreshView.NotifyRefreshing()` is now `internal` (was accidentally public). It's an
  implementation detail invoked by the platform handler once a pull clears the trigger threshold,
  not part of the public API — app code should use `Command`/`Refreshing` instead.
- iOS: `PullOverlayCurveView` reuses a single `UIBezierPath` across pull updates instead of
  allocating a new one on every frame.
- iOS: `PullOverlayCurveView` skips rebuilding the curve geometry when a pull update only changes
  opacity (pull distance and finger x-ratio unchanged), while still applying the opacity change
  inside the same zero-lag `CATransaction`.
- iOS: `PullOverlayCurveView` no longer falls back to `UIScreen.MainScreen.Bounds.Width` when its
  own bounds aren't laid out yet; it skips that draw instead, since guessing the screen width is
  wrong under Split View, Stage Manager, and mid-rotation layouts. `LayoutSubviews()` re-triggers
  the draw once real bounds land.

## [0.1.2] - 2026-08-24

### Added
- Support for wrapping non-scrollable content (e.g. a plain `Grid`). When no scrollable descendant
  is found, the handler wraps the content's own root view and treats "at top" as always true, so a
  downward drag from anywhere starts a pull immediately.

## [0.1.1] - 2026-08-24

### Added
- Repository metadata (`RepositoryUrl`, `RepositoryType`) for NuGet source link support.

### Fixed
- README demo GIF now resolves correctly when the package is viewed on NuGet.org.

## [0.1.0] - 2026-08-24

Initial release.

### Added
- `PullToRefreshView`: a native, elastic curved-strip pull-to-refresh control for .NET MAUI
  (iOS and Android), with independent native implementations of the same gesture state machine and
  curve math on each platform — not a spinner, not a wrapper around `RefreshView`.
- `Command`, `IsRefreshing`, and `Refreshing` event for driving refresh behavior from host pages.
- `StripColor` bindable property for customizing the curved strip's color and max opacity.
- Auto-discovery of scrollable content (`UIScrollView` on iOS; `RecyclerView`/`ScrollView` on
  Android) so the control can wrap `CollectionView`, `ScrollView`, and similar content directly.
- GitHub Actions workflow to publish the package to NuGet.org via Trusted Publishing (OIDC).
