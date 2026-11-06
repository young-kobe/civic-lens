namespace CivicLens.PublicApi.Officials;

/// <summary>The executive actions of the active release that name the official, by document type. <c>ActionsInRelease</c> is the base: every executive action of the active release.</summary>
public sealed record ExecutiveActionsFigure(string Scope, int ActionsInRelease, ExecutiveActionCount[] ByDocumentType);
