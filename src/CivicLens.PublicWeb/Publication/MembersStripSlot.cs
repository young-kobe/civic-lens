namespace CivicLens.PublicWeb.Publication;

/// <summary>A service that only the server shell registers. <see cref="PublicPage"/> then shows an empty slot for the members strip. A later step fills the slot in the browser from the ZIP on the device, so the cached page holds no reader data. The static release pages have no slot.</summary>
public sealed class MembersStripSlot
{
    public const string ElementId = "members-strip";
}
