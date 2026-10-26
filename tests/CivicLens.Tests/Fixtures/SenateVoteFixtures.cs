using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

/// <summary>Trimmed copies of senate.gov roll call files for the 119th Congress, session 1, fetched 2026-10-21. The tie-break fixture has the shape of vote 372.</summary>
internal static class SenateVoteFixtures
{
    public const string SourceId = "senate-votes";
    public const string SourceUrl = "https://www.senate.gov/legislative/LIS";
    public static readonly DateOnly Backfill = new(2025, 1, 3);
    public static readonly VoteSession Session = new(119, 1);
    public static readonly SenateVoteRoll Roll1 = new(Session, 1);

    public static byte[] Menu(string votes = DefaultMenuVotes, string congressYear = "2025") => Bytes($"""
        <?xml version="1.0" encoding="UTF-8"?><vote_summary>
          <congress>119</congress>
          <session>1</session>
          <congress_year>{congressYear}</congress_year>
          <votes>{votes}</votes>
        </vote_summary>
        """);

    public const string DefaultMenuVotes = """
        <vote>
          <vote_number>00659</vote_number>
          <vote_date>18-Dec</vote_date>
          <issue>PN373</issue>
          <question>On the Cloture Motion
             </question>
          <result>Agreed to</result>
          <vote_tally><yeas>51</yeas><nays>42</nays></vote_tally>
          <title>Motion to Invoke Cloture: Sara Bailey to be Director of National Drug Control Policy</title>
        </vote>
        <vote>
          <vote_number>00001</vote_number>
          <vote_date>09-Jan</vote_date>
          <issue>S. 5</issue>
          <question>On Cloture on the Motion to Proceed</question>
          <result>Agreed to</result>
          <vote_tally><yeas>84</yeas><nays>9</nays></vote_tally>
          <title>Motion to Invoke Cloture: Motion to Proceed to S. 5</title>
        </vote>
        """;

    public static byte[] Vote(string voteNumber = "1", string modifyDate = "January 10, 2025,  02:57 PM",
        string voteDate = "January 9, 2025,  02:54 PM", string document = DefaultDocument, string count = DefaultCount,
        string members = DefaultMembers, string question = "On Cloture on the Motion to Proceed",
        string tieBreaker = "<tie_breaker><by_whom/><tie_breaker_vote/></tie_breaker>") => Bytes($"""
        <?xml version="1.0" encoding="UTF-8"?><roll_call_vote>
          <congress>119</congress>
          <session>1</session>
          <congress_year>2025</congress_year>
          <vote_number>{voteNumber}</vote_number>
          <vote_date>{voteDate}</vote_date>
          <modify_date>{modifyDate}</modify_date>
          <vote_question_text>On Cloture on the Motion to Proceed S. 5</vote_question_text>
          <vote_document_text>A bill to require the Secretary of Homeland Security to take into custody aliens.</vote_document_text>
          <vote_result_text>Cloture on the Motion to Proceed Agreed to (84-9, 3/5 majority required)</vote_result_text>
          <question>{question}</question>
          <vote_title>Motion to Invoke Cloture: Motion to Proceed to S. 5</vote_title>
          <majority_requirement>3/5</majority_requirement>
          <vote_result>Cloture on the Motion to Proceed Agreed to</vote_result>
          {document}
          <amendment><amendment_number/><amendment_purpose>No Statement of Purpose on File.</amendment_purpose></amendment>
          {count}
          {tieBreaker}
          <members>{members}</members>
        </roll_call_vote>
        """);

    public const string DefaultDocument = """
        <document>
          <document_congress>119</document_congress>
          <document_type>S.</document_type>
          <document_number>5</document_number>
          <document_name>S. 5</document_name>
          <document_title>A bill to require the Secretary of Homeland Security to take into custody aliens.</document_title>
          <document_short_title/>
        </document>
        """;

    public const string DefaultCount = "<count><yeas>1</yeas><nays>0</nays><present/><absent>1</absent></count>";

    public const string DefaultMembers = """
        <member>
          <member_full>Alsobrooks (D-MD)</member_full>
          <last_name>Alsobrooks</last_name>
          <first_name>Angela</first_name>
          <party>D</party>
          <state>MD</state>
          <vote_cast>Yea</vote_cast>
          <lis_member_id>S428</lis_member_id>
        </member>
        <member>
          <member_full>Banks (R-IN)</member_full>
          <last_name>Banks</last_name>
          <first_name>Jim</first_name>
          <party>R</party>
          <state>IN</state>
          <vote_cast>Not Voting</vote_cast>
          <lis_member_id>S429</lis_member_id>
        </member>
        """;

    public static CollectionConfiguration Configuration(DiscoveryAdmissionPolicy? policy = null) => new()
    {
        Version = 1,
        People = [new PersonConfiguration { Id = "with-id-a", Name = "A", BioguideId = "A000001" }],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = SourceId,
                Url = SourceUrl,
                AllowedOrigin = "https://www.senate.gov",
                AllowedPathPrefix = "/legislative/LIS",
                Mode = CollectionMode.Api,
                ApiParser = ApiParser.SenateVoteList,
                MinimumCongress = 119,
                BackfillFrom = Backfill,
                AdmissionPolicy = policy ?? new DiscoveryAdmissionPolicy
                {
                    MaxJobs = 1000,
                    MaxTotalRequests = 4000,
                    MaxTotalBytes = 2_000_000_000,
                    MaxTotalTimeoutSeconds = 20_000
                },
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2 },
                MaxRequests = 2,
                MaxBytes = 1_000_000,
                TimeoutSeconds = 10
            }
        ]
    };

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);
}
