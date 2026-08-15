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
    private DateTimeOffset? _displayUntil;
    private DateTimeOffset? _fadeUntil;

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
    public IndicatorViewState Observe(InputContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (IsStale(snapshot))
        {
            return _state;
        }

        var isNewGeneration = _lastContext is null || snapshot.Generation > _lastContext.Generation;
        var effectiveTime = LaterOf(_currentTime, snapshot.ObservedAt);
        AdvanceCore(effectiveTime);

        var previousContext = _lastContext;
        _lastContext = snapshot;
        _currentTime = effectiveTime;

        var hiddenReason = GetHiddenReason(snapshot);
        if (hiddenReason is not null)
        {
            Hide(snapshot.Generation, hiddenReason.Value);
            return _state;
        }

        var shouldReplay = isNewGeneration ||
            previousContext is null ||
            previousContext.InputState != snapshot.InputState ||
            previousContext.Eligibility != Eligibility.EditableCaret;

        if (shouldReplay)
        {
            Show(
                snapshot,
                previousContext is not null && previousContext.InputState != snapshot.InputState
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
                InputState = snapshot.InputState,
                Anchor = snapshot.Anchor,
            };
        }

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
        if (snapshot.Eligibility is not Eligibility.EditableCaret)
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

        if (_options.AlwaysVisible)
        {
            _displayUntil = null;
            _fadeUntil = null;
            return;
        }

        _displayUntil = now + _options.DisplayDuration;
        _fadeUntil = _displayUntil + _options.FadeDuration;
        AdvanceCore(now);
    }

    private void Hide(long generation, IndicatorReasonCode reasonCode)
    {
        _displayUntil = null;
        _fadeUntil = null;
        _state = IndicatorViewState.Hidden(generation, reasonCode);
    }

    private void AdvanceCore(DateTimeOffset now)
    {
        if (_options.AlwaysVisible || !_state.IsVisible || _displayUntil is null || _fadeUntil is null)
        {
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

    private static DateTimeOffset LaterOf(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;
}
