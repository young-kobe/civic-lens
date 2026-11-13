namespace CivicLens.PublicWeb.Publication;

/// <summary>What the server page responder gives to the shell of every server page: the newest published roll call day of each chamber.
/// A page rendered with this context is a gated preview page. The static pages have none. A null day means the chamber has no published roll call.</summary>
public sealed record ServerPageContext(DateOnly? NewestHouseRollCall, DateOnly? NewestSenateRollCall);
