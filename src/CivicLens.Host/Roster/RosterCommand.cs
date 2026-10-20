using CivicLens.Application.Collection;
using CivicLens.Application.Roster;

namespace CivicLens.Host.Roster;

internal static class RosterCommand
{
    private const string Usage = "Use roster propose <config.json>.";
    private const string PressSourcesNote = "Add press sources by hand. This step is manual.";

    public static bool Matches(string[] args) => args is ["roster", ..];

    public static async Task<int> ExecuteAsync(string[] args, CollectionConfiguration? configuration, ProposeRosterChanges propose,
        CancellationToken cancellationToken)
    {
        if (args is not ["roster", "propose", _] || configuration is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        RosterProposal? proposal;
        try
        {
            proposal = await propose.ExecuteAsync(configuration, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        if (proposal is null)
        {
            Console.Error.WriteLine("No roster snapshot exists. Enable the member roster source and let the worker collect it.");
            return 1;
        }
        CommandOutput.Write(new
        {
            joins = proposal.Joins.Select(member => new { member.Id, member.Name, member.BioguideId, note = PressSourcesNote }),
            leaves = proposal.Leaves.Select(person => new { person.Id, person.Name, person.BioguideId })
        });
        return 0;
    }
}
