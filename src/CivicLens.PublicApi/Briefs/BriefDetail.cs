using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Briefs;

public sealed record BriefDetail(int Release, BriefRow Brief, PublishedBriefParagraph[] Paragraphs, PublishedBriefCitation[] Citations);
