namespace CivicLens.PublicWeb.Publication;

/// <summary>What the server page responder gives to the shell of every server page: the newest published roll call day of each chamber.
/// A null day means the chamber has no published roll call.</summary>
public sealed record ServerPageContext(DateOnly? NewestHouseRollCall, DateOnly? NewestSenateRollCall);
