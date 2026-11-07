using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Components.Ui;

public sealed record FeedItem(string Text, DateTimeOffset Time, Tone Tone);
