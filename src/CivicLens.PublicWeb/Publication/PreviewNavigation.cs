namespace CivicLens.PublicWeb.Publication;

/// <summary>Marks the server-rendered preview. <c>PublicApi</c> registers it, and the static renderer does not. <see cref="PublicPage"/> then shows
/// <see cref="PublicSections.PreviewHeader"/>, whose links lead to the gated preview lists.</summary>
public sealed class PreviewNavigation;
