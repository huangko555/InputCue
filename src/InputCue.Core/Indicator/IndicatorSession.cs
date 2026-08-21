using InputCue.Core.InputContext;

namespace InputCue.Core.Indicator;

/// <summary>
/// Converts serialized input-context observations and time advances into render-only view state.
/// Callers must invoke this module from one serialized execution path.
/// </summary>
public sealed class IndicatorSession
{
    private readonly IndicatorSessionOptions _options;
    private IndicatorViewState _state = IndicatorViewState.Initial;
    private InputContextSnapshot? _lastContext;
    private DateTimeOffset _currentTime = DateTimeOffset.MinValue;
    private DateTimeOffset? _displayStartedAt;
    private DateTimeOffset? _displayUntil;
    private DateTimeOffset? _fadeUntil;
    private DateTimeOffset? _lastInputActivityAt;
    private InputState? _lastDisplayEligibleInputState;
    private bool _textActivityPending;

    public IndicatorSession(IndicatorSessionOptions? options = null)
    {
        _options = options ?? IndicatorSessionOptions.Default;
        _options.Validate();
    }

    public IndicatorViewState Current => _state;

    /// <summary>
    /// Applies an observation. Older generations and observations with an earlier timestamp in the
    /// current generation are ignored without changing the current view state.
    /// </summary>
    public IndicatorViewState Observe(InputContextSnapshot snapshot) =>
        Observe(
            snapshot,
            receivedAt: null,
            refreshAnchor: false,
            suppressContextReplay: false);

    public IndicatorViewState Observe(
        InputContextSnapshot snapshot,
        DateTimeOffset? receivedAt,
        bool refreshAnchor = false,
        bool suppressContextReplay = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (IsStale(snapshot))
        {
            return _state;
        }

        var isNewGeneration = _lastContext is null || snapshot.Generation > _lastContext.Generation;
        var effectiveTime = LaterOf(
            _currentTime,
            snapshot.ObservedAt,
            receivedAt ?? DateTimeOffset.MinValue);
        AdvanceCore(effectiveTime);

        var previousContext = _lastContext;
        _lastContext = snapshot with { ObservedAt = effectiveTime };
        _currentTime = effectiveTime;

        var hiddenReason = GetHiddenReason(snapshot);
        if (hiddenReason is not null)
        {
            Hide(snapshot.Generation, hiddenReason.Value);
            return _state;
        }

        var inputStateChanged = _lastDisplayEligibleInputState is { } previousInputState &&
            previousInputState != snapshot.InputState;
        var contextEstablished = isNewGeneration ||
            previousContext is null ||
            !IsDisplayEligible(previousContext.Eligibility);
        _lastDisplayEligibleInputState = snapshot.InputState;
        var shouldReplay = inputStateChanged || contextEstablished;
        var shouldSuppressContextReplay = contextEstablished &&
            !inputStateChanged &&
            (IsContextReplaySuppressed(effectiveTime) ||
                suppressContextReplay && !_options.AlwaysVisible);

        if (shouldReplay && !shouldSuppressContextReplay)
        {
            Show(
                snapshot,
                inputStateChanged
                    ? IndicatorReasonCode.InputStateChanged
                    : IndicatorReasonCode.ContextEstablished,
                effectiveTime);
            return _state;
        }

        if (_state.IsVisible)
        {
            _state = _state with
            {
                Generation = snapshot.Generation,
                Anchor = refreshAnchor ? snapshot.Anchor : _state.Anchor,
            };
        }

        return _state;
    }

    /// <summary>
    /// Applies an anonymous editing-key activity without recording the key or any resulting text.
    /// </summary>
    public IndicatorViewState ObserveInputActivity(DateTimeOffset receivedAt)
    {
        if (receivedAt <= _currentTime)
        {
            return _state;
        }

        _currentTime = receivedAt;
        AdvanceCore(receivedAt);
        _lastInputActivityAt = receivedAt;
        if (_options.AlwaysVisible || !_state.IsVisible)
        {
            return _state;
        }

        var minimumDisplayUntil = _displayStartedAt is { } displayStartedAt
            ? displayStartedAt + _options.MinimumDisplayDuration
            : receivedAt;
        if (receivedAt < minimumDisplayUntil)
        {
            _textActivityPending = true;
            return _state;
        }

        Hide(_state.Generation, IndicatorReasonCode.InputActivityDetected);
        return _state;
    }

    /// <summary>
    /// Advances transient display timing. Time values at or before the latest accepted time are ignored.
    /// </summary>
    public IndicatorViewState Advance(DateTimeOffset now)
    {
        if (now <= _currentTime)
        {
            return _state;
        }

        _currentTime = now;
        AdvanceCore(now);
        return _state;
    }

    private bool IsStale(InputContextSnapshot snapshot)
    {
        if (_lastContext is null)
        {
            return false;
        }

        return snapshot.Generation < _lastContext.Generation ||
            snapshot.Generation == _lastContext.Generation &&
            snapshot.ObservedAt < _lastContext.ObservedAt;
    }

    private static IndicatorReasonCode? GetHiddenReason(InputContextSnapshot snapshot)
    {
        if (!IsDisplayEligible(snapshot.Eligibility))
        {
            return snapshot.Eligibility is Eligibility.PositionUnknown
                ? IndicatorReasonCode.PositionUnavailable
                : IndicatorReasonCode.ContextIneligible;
        }

        if (snapshot.InputState is InputState.Unknown)
        {
            return IndicatorReasonCode.InputStateUnknown;
        }

        if (snapshot.Anchor is not { IsUsable: true })
        {
            return IndicatorReasonCode.PositionUnavailable;
        }

        return null;
    }

    private static bool IsDisplayEligible(Eligibility eligibility) =>
        eligibility is Eligibility.EditableCaret or Eligibility.EditableSelection;

    private bool IsContextReplaySuppressed(DateTimeOffset now) =>
        _lastInputActivityAt is { } lastInputActivityAt &&
        now < lastInputActivityAt + _options.ContextReplaySuppressionDuration;

    private void Show(
        InputContextSnapshot snapshot,
        IndicatorReasonCode reasonCode,
        DateTimeOffset now)
    {
        _state = new IndicatorViewState(
            snapshot.Generation,
            IndicatorPhase.Visible,
            snapshot.InputState,
            snapshot.Anchor,
            1,
            reasonCode);
        _displayStartedAt = now;
        _textActivityPending = false;

        if (_options.AlwaysVisible)
        {
            _displayUntil = null;
            _fadeUntil = null;
            return;
        }

        var displayDuration = _options.DisplayDuration < _options.MinimumDisplayDuration
            ? _options.MinimumDisplayDuration
            : _options.DisplayDuration;
        _displayUntil = now + displayDuration;
        _fadeUntil = _displayUntil + _options.FadeDuration;
        AdvanceCore(now);
    }

    private void Hide(long generation, IndicatorReasonCode reasonCode)
    {
        _displayStartedAt = null;
        _displayUntil = null;
        _fadeUntil = null;
        _textActivityPending = false;
        _state = IndicatorViewState.Hidden(generation, reasonCode);
    }

    private void AdvanceCore(DateTimeOffset now)
    {
        if (_options.AlwaysVisible || !_state.IsVisible || _displayUntil is null || _fadeUntil is null)
        {
            return;
        }

        if (_textActivityPending &&
            _displayStartedAt is { } displayStartedAt &&
            now >= displayStartedAt + _options.MinimumDisplayDuration)
        {
            Hide(_state.Generation, IndicatorReasonCode.InputActivityDetected);
            return;
        }

        if (now < _displayUntil.Value)
        {
            return;
        }

        if (_options.FadeDuration == TimeSpan.Zero || now >= _fadeUntil.Value)
        {
            Hide(_state.Generation, IndicatorReasonCode.FadeCompleted);
            return;
        }

        var remaining = (_fadeUntil.Value - now).TotalMilliseconds;
        var opacity = remaining / _options.FadeDuration.TotalMilliseconds;
        _state = _state with
        {
            Phase = IndicatorPhase.Fading,
            Opacity = Math.Clamp(opacity, 0, 1),
            ReasonCode = IndicatorReasonCode.DisplayDurationElapsed,
        };
    }

    private static DateTimeOffset LaterOf(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first >= second
            ? first >= third ? first : third
            : second >= third ? second : third;
}
