using CivicLens.PublicApi;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

public sealed class PublicApiOptionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void ARateLimitBelowOneOrNotANumberIsRefusedAtStartupBecauseZeroWouldBlockEveryReader(string value)
    {
        Assert.Throws<ArgumentException>(() => PublicApiOptions.ParseLimit(PublicApiOptions.RequestsPerMinuteVariable, value, 60));
    }

    [Fact]
    public void AMissingRateLimitMeansSixtyAndAValidOneIsKept()
    {
        Assert.Equal(60, PublicApiOptions.ParseLimit(PublicApiOptions.RequestsPerMinuteVariable, null, 60));
        Assert.Equal(7, PublicApiOptions.ParseLimit(PublicApiOptions.RequestsPerMinuteVariable, "7", 60));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void APullTokenUnder32CharactersIsRefusedAtStartupBecauseItGuardsEveryReport(string token)
    {
        Assert.Throws<ArgumentException>(() => PublicApiOptions.ParsePullToken(token));
    }

    [Fact]
    public void AMissingPullTokenMeansNoPullRouteAndALongOneIsKept()
    {
        Assert.Null(PublicApiOptions.ParsePullToken(null));
        Assert.Equal(new string('t', 32), PublicApiOptions.ParsePullToken(new string('t', 32)));
    }

    [Fact]
    public void ThePublicViewsConnectionKeepsItsLoginAndCapsThePoolAtFourBecauseTheRoleAllowsFiveConnections()
    {
        var options = new PublicApiOptions
        {
            Database = "Host=search;Database=search",
            PublicViewsDatabase = "Host=operational;Database=civic_lens;Username=civic_lens_public;Password=secret"
        };

        var views = new NpgsqlConnectionStringBuilder(options.PublicViewsReaderDatabase);

        Assert.Equal(4, views.MaxPoolSize);
        Assert.Equal(("operational", "civic_lens_public"), (views.Host, views.Username));
    }

    [Fact]
    public void ThePublicViewsPoolClosesIdleConnectionsBeforeTheRoleDoesBecauseAClosedConnectionFailsTheNextRequest()
    {
        const int RoleIdleSessionSeconds = 60;
        var options = new PublicApiOptions
        {
            Database = "Host=search;Database=search",
            PublicViewsDatabase = "Host=operational;Database=civic_lens;Username=civic_lens_public;Password=secret;Minimum Pool Size=2;Connection Pruning Interval=60"
        };

        var views = new NpgsqlConnectionStringBuilder(options.PublicViewsReaderDatabase);

        Assert.Equal(0, views.MinPoolSize);
        Assert.Equal(30, views.ConnectionIdleLifetime);
        Assert.Equal(PublicApiOptions.PublicViewsPruningIntervalSeconds, views.ConnectionPruningInterval);
        Assert.True(views.ConnectionIdleLifetime + views.ConnectionPruningInterval < RoleIdleSessionSeconds);
    }
}
