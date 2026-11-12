using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One reached step of a bill. The title is a fixed approved phrase. The detail holds the date and the facts that the step names. The chamber is set when the step happened in one chamber.</summary>
public sealed record BillStepData(string Title, string Detail)
{
    public PublishedChamber? Chamber { get; init; }
}
