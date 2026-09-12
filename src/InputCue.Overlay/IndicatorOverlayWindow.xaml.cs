using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using InputCue.Core.Indicator;
using InputCue.Core.InputContext;
using InputCue.Core.Settings;

namespace InputCue.Overlay;

public partial class IndicatorOverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const int TransparentStyle = 0x00000020;
    private const uint NoActivatePosition = 0x0010;
    private const uint NoSizePosition = 0x0001;
    private const uint NoMovePosition = 0x0002;
    private const uint NoZOrderPosition = 0x0004;
    private const uint ShowWindowPosition = 0x0040;
    private const int TopMostWindow = -1;
    private const int NotTopMostWindow = -2;
    private const int AnchorGapDip = 6;
    private const uint MonitorDefaultToNearest = 0x00000002;
    internal const int FlipCollapseMilliseconds = 70;
    internal const int FlipExpandMilliseconds = 80;
    internal const int FlipAppearanceMilliseconds =
        (FlipCollapseMilliseconds + FlipExpandMilliseconds) / 2;
    private static readonly Duration FlipCollapseDuration = new(
        TimeSpan.FromMilliseconds(FlipCollapseMilliseconds));
    private static readonly Duration FlipExpandDuration = new(
        TimeSpan.FromMilliseconds(FlipExpandMilliseconds));

    private readonly IndicatorVisualRenderer _renderer;
    private nint _windowHandle;
    private long? _positionedGeneration;
    private ScreenRect? _positionedAnchor;
    private IndicatorPlacement _placement = InputCueSettings.DefaultPlacement;
    private int _horizontalOffsetDip;
    private int _verticalOffsetDip;
    private IndicatorTransitionAnimation _transitionAnimation;
    private InputState? _renderedInputState;
    private InputState? _pendingInputState;
    private bool _isIndicatorVisible;
    private bool _isAppearanceExpanding;
    private PrimedAppearance? _primedAppearance;
    private int _primingRenderCount;
    private bool _isAppearanceRenderingSubscribed;
    private long _transitionVersion;

    internal ScreenRect? PositionedAnchor => _positionedAnchor;

    internal IndicatorOverlayWindow()
    {
        InitializeComponent();
        _renderer = new IndicatorVisualRenderer(
            IndicatorDot,
            LightBadgeVisual,
            LightBadgeShadow,
            LightBadgeBody,
            LightBadgeGlyphViewbox,
            LightBadgeGlyph,
            LightBadgeCustomIcon);
    }

    internal void Configure(
        IndicatorStyle style,
        IndicatorPlacement placement,
        int horizontalOffsetDip,
        int verticalOffsetDip,
        int indicatorSizeDip,
        int lightBadgeSizeDip,
        IndicatorTransitionAnimation transitionAnimation,
        string chineseDotColor,
        string englishDotColor,
        string englishUsDotColor,
        string capsLockDotColor)
    {
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        if (horizontalOffsetDip is < InputCueSettings.MinimumOffsetDip or > InputCueSettings.MaximumOffsetDip)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalOffsetDip));
        }

        if (verticalOffsetDip is < InputCueSettings.MinimumOffsetDip or > InputCueSettings.MaximumOffsetDip)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalOffsetDip));
        }

        if (!Enum.IsDefined(transitionAnimation))
        {
            throw new ArgumentOutOfRangeException(nameof(transitionAnimation));
        }

        CancelTransition();
        _renderer.Configure(
            style,
            indicatorSizeDip,
            lightBadgeSizeDip,
            chineseDotColor,
            englishDotColor,
            englishUsDotColor,
            capsLockDotColor);
        _placement = placement;
        _horizontalOffsetDip = horizontalOffsetDip;
        _verticalOffsetDip = verticalOffsetDip;
        _transitionAnimation = transitionAnimation;
        _renderedInputState = null;
        Width = _renderer.RootWidth;
        Height = _renderer.RootHeight;
        _positionedGeneration = null;
        _positionedAnchor = null;
    }

    internal void UpdateCustomIcons(CustomIconImages images)
    {
        _renderer.UpdateCustomIcons(images);
    }

    internal void UpdateCustomShadow(CustomIconShadowMode mode)
    {
        _renderer.UpdateCustomShadow(mode);
    }

    internal void Render(
        IndicatorViewState state,
        bool targetChanged = false,
        bool contextActivated = false)
    {
        if (!state.IsVisible || state.Anchor is not { IsUsable: true } anchor)
        {
            HideIndicator();
            _positionedGeneration = null;
            _positionedAnchor = null;
            return;
        }

        var isTargetHandoff = targetChanged &&
            _positionedGeneration != state.Generation;
        if (_primedAppearance is not null)
        {
            if (targetChanged &&
                _primedAppearance.Generation != state.Generation)
            {
                BeginPrimedAppearance(state, anchor, deferTargetMove: true);
            }
            else
            {
                UpdatePrimedAppearance(state, anchor);
            }

            return;
        }

        if (isTargetHandoff &&
            _isIndicatorVisible &&
            _transitionAnimation == IndicatorTransitionAnimation.Flip)
        {
            BeginPrimedAppearance(state, anchor, deferTargetMove: true);
            return;
        }

        var shouldAnimateAppearance = OverlayRenderPolicy.ShouldAnimateAppearance(
            _transitionAnimation,
            _isIndicatorVisible);
        if (shouldAnimateAppearance)
        {
            BeginPrimedAppearance(state, anchor, deferTargetMove: false);
            return;
        }

        var desiredOpacity = state.Opacity;
        if (!_isIndicatorVisible)
        {
            Opacity = 0;
        }

        if (_pendingInputState != state.InputState)
        {
            if (_isAppearanceExpanding)
            {
                RetargetFlipAppearance(state.InputState);
            }
            else if (OverlayRenderPolicy.ShouldAnimateTransition(
                    _transitionAnimation,
                    _isIndicatorVisible,
                    _renderedInputState,
                    state.InputState,
                    state.ReasonCode))
            {
                BeginFlip(state.InputState);
            }
            else
            {
                RenderImmediately(state.InputState);
            }
        }

        if (_isIndicatorVisible)
        {
            Opacity = desiredOpacity;
        }

        var shouldReposition = OverlayRenderPolicy.ShouldReposition(
            _isIndicatorVisible,
            _positionedGeneration,
            state.Generation,
            _positionedAnchor,
            anchor,
            contextActivated);
        if (shouldReposition)
        {
            var wasVisible = _isIndicatorVisible;
            var preserveZOrder = OverlayRenderPolicy.ShouldPreserveZOrder(
                wasVisible,
                isTargetHandoff,
                contextActivated);
            if (!wasVisible)
            {
                _isIndicatorVisible = true;
                if (!IsVisible)
                {
                    Show();
                }
            }

            Position(anchor, preserveZOrder);
            _positionedGeneration = state.Generation;
            _positionedAnchor = anchor;
            Opacity = desiredOpacity;
        }
    }

    internal void HideIndicator()
    {
        CancelTransition();
        _renderedInputState = null;
        _isIndicatorVisible = false;
        Opacity = 0;
        IndicatorScale.ScaleX = 0;
    }

    private void RenderImmediately(InputState state)
    {
        CancelTransition();
        _renderer.Render(state);
        _renderedInputState = state;
    }

    private void BeginFlip(InputState nextState)
    {
        var initialScale = Math.Clamp(IndicatorScale.ScaleX, 0, 1);
        CancelTransition();
        _pendingInputState = nextState;
        var version = _transitionVersion;
        var collapse = new DoubleAnimation(initialScale, 0, FlipCollapseDuration)
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        collapse.Completed += (_, _) =>
        {
            if (version != _transitionVersion || _pendingInputState != nextState)
            {
                return;
            }

            IndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _renderer.Render(nextState);
            _renderedInputState = nextState;
            IndicatorScale.ScaleY = 1;
            BeginFlipExpand(nextState, version, 0, isAppearance: false);
        };
        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            collapse,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void BeginPrimedAppearance(
        IndicatorViewState state,
        ScreenRect anchor,
        bool deferTargetMove)
    {
        // For a visible target handoff, the old surface must become transparent
        // before SetWindowPos moves the HWND. Otherwise DWM can briefly present
        // the previous full-width surface at the new target.
        Opacity = 0;
        CancelTransition();
        IndicatorScale.ScaleX = 0;
        _renderer.Render(state.InputState);
        _renderedInputState = state.InputState;
        _pendingInputState = state.InputState;
        _isAppearanceExpanding = true;
        _primedAppearance = new PrimedAppearance(
            state.Generation,
            state.InputState,
            anchor,
            state.Opacity,
            deferTargetMove,
            TargetPositioned: !deferTargetMove);
        _primingRenderCount = 0;
        _isIndicatorVisible = true;
        if (!IsVisible)
        {
            Show();
        }

        if (!deferTargetMove)
        {
            Position(anchor, preserveZOrder: false);
            _positionedGeneration = state.Generation;
            _positionedAnchor = anchor;
        }

        SubscribeAppearanceRendering();
    }

    private void UpdatePrimedAppearance(IndicatorViewState state, ScreenRect anchor)
    {
        _renderer.Render(state.InputState);
        _renderedInputState = state.InputState;
        _pendingInputState = state.InputState;
        var appearance = _primedAppearance!;
        _primedAppearance = new PrimedAppearance(
            state.Generation,
            state.InputState,
            anchor,
            state.Opacity,
            appearance.DeferTargetMove,
            appearance.TargetPositioned);
        if (appearance.TargetPositioned &&
            (_positionedGeneration != state.Generation || _positionedAnchor != anchor))
        {
            Position(anchor, preserveZOrder: true);
            _positionedGeneration = state.Generation;
            _positionedAnchor = anchor;
        }
    }

    private void SubscribeAppearanceRendering()
    {
        if (_isAppearanceRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += OnAppearanceRendering;
        _isAppearanceRenderingSubscribed = true;
    }

    private void OnAppearanceRendering(object? sender, EventArgs e)
    {
        AdvanceAppearanceFrame();
    }

    internal void AdvanceAppearanceFrame()
    {
        _primingRenderCount++;
        if (_primedAppearance is not { } appearance || !_isIndicatorVisible)
        {
            UnsubscribeAppearanceRendering();
            _primedAppearance = null;
            return;
        }

        if (!appearance.TargetPositioned)
        {
            if (_primingRenderCount < 2)
            {
                return;
            }

            Position(appearance.Anchor, preserveZOrder: false);
            _positionedGeneration = appearance.Generation;
            _positionedAnchor = appearance.Anchor;
            _primedAppearance = appearance with { TargetPositioned = true };
            return;
        }

        var requiredRenderCount = appearance.DeferTargetMove ? 3 : 2;
        if (_primingRenderCount < requiredRenderCount)
        {
            return;
        }

        UnsubscribeAppearanceRendering();
        _primedAppearance = null;
        Opacity = appearance.Opacity;
        BeginFlipExpand(
            appearance.InputState,
            _transitionVersion,
            0,
            isAppearance: true);
    }

    private void UnsubscribeAppearanceRendering()
    {
        if (!_isAppearanceRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnAppearanceRendering;
        _isAppearanceRenderingSubscribed = false;
    }

    private void RetargetFlipAppearance(InputState state)
    {
        var initialScale = Math.Clamp(IndicatorScale.ScaleX, 0, 1);
        CancelTransition();
        _renderer.Render(state);
        _renderedInputState = state;
        _pendingInputState = state;
        _isAppearanceExpanding = true;
        BeginFlipExpand(state, _transitionVersion, initialScale, isAppearance: true);
    }

    private void BeginFlipExpand(
        InputState state,
        long version,
        double initialScale,
        bool isAppearance)
    {
        // Keep the base value collapsed before the first animation tick. Otherwise
        // Show() can paint one full-width frame before the animation takes control.
        initialScale = Math.Clamp(initialScale, 0, 1);
        IndicatorScale.ScaleX = initialScale;
        var remainingDuration = isAppearance
            ? new Duration(TimeSpan.FromMilliseconds(
                Math.Max(1, FlipAppearanceMilliseconds * (1 - initialScale))))
            : FlipExpandDuration;
        var expand = new DoubleAnimation(initialScale, 1, remainingDuration)
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        expand.Completed += (_, _) =>
        {
            if (version == _transitionVersion && _pendingInputState == state)
            {
                IndicatorScale.ScaleX = 1;
                IndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _pendingInputState = null;
                _isAppearanceExpanding = false;
            }
        };
        IndicatorScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            expand,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelTransition()
    {
        UnsubscribeAppearanceRendering();
        _primedAppearance = null;
        _primingRenderCount = 0;
        _transitionVersion++;
        _pendingInputState = null;
        _isAppearanceExpanding = false;
        IndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IndicatorScale.ScaleX = 1;
    }

    private sealed record PrimedAppearance(
        long Generation,
        InputState InputState,
        ScreenRect Anchor,
        double Opacity,
        bool DeferTargetMove,
        bool TargetPositioned);

    protected override void OnClosed(EventArgs e)
    {
        UnsubscribeAppearanceRendering();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(_windowHandle, ExtendedStyleIndex).ToInt64();
        _ = SetWindowLongPtr(
            _windowHandle,
            ExtendedStyleIndex,
            new nint(extendedStyle | NoActivateStyle | ToolWindowStyle | TransparentStyle));
    }

    private void Position(ScreenRect anchor, bool preserveZOrder)
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var monitor = MonitorFromRect(NativeRect.From(anchor), MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        // UIA, MSAA and ClientToScreen anchors are physical screen pixels. Keep them
        // in that coordinate space and scale only the WPF-authored visual gap.
        var dpi = GetMonitorDpi(monitor);
        var overlaySize = new PixelSize(
            OverlayPlacement.ScaleDipToPixels(ActualWidth, dpi),
            OverlayPlacement.ScaleDipToPixels(ActualHeight, dpi));
        var position = OverlayPlacement.Calculate(
            anchor,
            monitorInfo.WorkArea.ToPixelRect(),
            overlaySize,
            OverlayPlacement.ScaleDipToPixels(AnchorGapDip, dpi),
            _placement,
            OverlayPlacement.ScaleDipToPixels(_horizontalOffsetDip, dpi),
            OverlayPlacement.ScaleDipToPixels(_verticalOffsetDip, dpi));
        var positionFlags = NoActivatePosition | NoSizePosition | ShowWindowPosition;
        _ = SetWindowPos(
            _windowHandle,
            preserveZOrder ? 0 : TopMostWindow,
            position.X,
            position.Y,
            0,
            0,
            positionFlags | (preserveZOrder ? NoZOrderPosition : 0));
        if (!preserveZOrder)
        {
            // A non-activating WPF window launched from a background process can be
            // placed behind the foreground app. Briefly enter the topmost band, then
            // immediately return to the normal band at its front.
            _ = SetWindowPos(
                _windowHandle,
                NotTopMostWindow,
                0,
                0,
                0,
                0,
                NoActivatePosition | NoMovePosition | NoSizePosition);
        }
    }

    private uint GetMonitorDpi(nint monitor)
    {
        const int effectiveDpi = 0;
        if (GetDpiForMonitor(monitor, effectiveDpi, out var dpiX, out _) >= 0 && dpiX > 0)
        {
            return dpiX;
        }

        var windowDpi = GetDpiForWindow(_windowHandle);
        return windowDpi > 0 ? windowDpi : 96;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint window, int index, nint newValue);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(in NativeRect rectangle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);

        internal static NativeRect From(ScreenRect rectangle) => new()
        {
            Left = checked((int)Math.Round(rectangle.X)),
            Top = checked((int)Math.Round(rectangle.Y)),
            Right = checked((int)Math.Round(rectangle.X + rectangle.Width)),
            Bottom = checked((int)Math.Round(rectangle.Y + rectangle.Height)),
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;

        internal static MonitorInfo Create() => new()
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
    }
}
