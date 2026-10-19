namespace CivicLens.Application.Review;

public sealed class DraftLookupFormatException()
    : ArgumentException("Enter a bill such as HR 1234 or 118 HR 1234, or a roll call such as Roll 245.");
