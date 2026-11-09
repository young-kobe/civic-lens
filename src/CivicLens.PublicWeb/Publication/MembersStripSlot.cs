namespace CivicLens.PublicWeb.Publication;

/// <summary>The server shell's public API origin for a device-filled members slot. Cached page markup holds no ZIP or member data.</summary>
public sealed class MembersStripSlot
{
    public const string ElementId = "members-strip";
    public string? BrowserApiRoot { get; init; }
}
