using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The officials file of a release, and the sort name of each official that a roster names. The sort names stay out of the file.</summary>
internal sealed record ResolvedOfficials(PublishedOfficials Published, IReadOnlyDictionary<string, string> SortNames);
