namespace CivicLens.Application.Actions;

internal sealed record ActionDraftOutput(ActionDraftOutputText Headline, ActionDraftOutputText Summary);

internal sealed record ActionDraftOutputText(string Text, double Confidence);
