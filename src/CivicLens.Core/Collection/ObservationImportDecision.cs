namespace CivicLens.Core.Collection;

public enum ObservationImportTransition
{
    NewObservation,
    Replay
}

public enum RepresentationBinding
{
    NotApplicable,
    Unresolved,
    Linked
}

/// <summary>Pure import decision. The observation remains the exact attempt; a 304 representation is a separate link.</summary>
public sealed class ObservationImportDecision
{
    internal ObservationImportDecision(Observation observation, CapturedObservation? capturedRepresentation,
        ObservationImportTransition transition, SentValidators? sentValidators)
    {
        Observation = observation;
        CapturedRepresentation = capturedRepresentation;
        Transition = transition;
        SentValidators = sentValidators;
        RepresentationBinding = observation is not NotModifiedObservation
            ? RepresentationBinding.NotApplicable
            : capturedRepresentation is null ? RepresentationBinding.Unresolved : RepresentationBinding.Linked;
    }

    public Observation Observation { get; }
    public CapturedObservation? CapturedRepresentation { get; }
    public ObservationImportTransition Transition { get; }
    public RepresentationBinding RepresentationBinding { get; }
    public SentValidators? SentValidators { get; }
    public CaptureIdentity? Capture => Observation is CapturedObservation captured
        ? captured.Capture
        : CapturedRepresentation?.Capture;
}
