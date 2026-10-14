using System.Text.Json.Serialization;

namespace CivicLens.Core.Actions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionSponsorshipRole { Sponsor, Cosponsor }
