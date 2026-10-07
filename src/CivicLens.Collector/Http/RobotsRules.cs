using System.Text;
using CivicLens.Collection.Contracts;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace CivicLens.Collector.Http;

internal sealed class RobotsRules
{
    private const int MaximumRuleLength = 2048;
    private const int MaximumRules = 10_000;
    private const int MaximumGroups = 10_000;
    private const int MaximumAgents = 10_000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    private readonly List<Rule> rules;

    public static RobotsRules Empty { get; } = new([], null);

    private RobotsRules(List<Rule> rules, long? crawlDelayMilliseconds)
    {
        this.rules = rules;
        CrawlDelayMilliseconds = crawlDelayMilliseconds;
    }

    public long? CrawlDelayMilliseconds { get; }

    public bool Allowed(string path, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeUnreserved(path, pattern: false);
        Rule? bestMatch = null;
        var matchingBudget = Stopwatch.StartNew();
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matchingBudget.Elapsed > TimeSpan.FromMilliseconds(250))
                throw new TimeoutException("Robots rule matching exceeded its time limit.");
            if (!RuleMatches(rule.Path, normalizedPath, cancellationToken)) continue;
            if (bestMatch is null || rule.Specificity > bestMatch.Specificity ||
                (rule.Specificity == bestMatch.Specificity && rule.Allow && !bestMatch.Allow))
                bestMatch = rule;
        }
        return bestMatch is null || bestMatch.Allow;
    }

    public static RobotsRules Parse(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var groups = new List<Group>();
        var group = new Group();
        var hasDirectives = false;
        var totalRules = 0;
        var totalAgents = 0;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rawLine.Length > MaximumRuleLength + 256)
                throw new FormatException("Robots line exceeds the supported limit.");
            var clean = rawLine.Split('#', 2)[0].Trim();
            if (clean.Length == 0) continue;

            var separator = clean.IndexOf(':');
            if (separator < 0) continue;
            var key = clean[..separator].Trim();
            var value = clean[(separator + 1)..].Trim();
            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (hasDirectives) FinishGroup();
                if (value.Length > 256) continue;
                group.UserAgents.Add(value);
                if (++totalAgents > MaximumAgents) throw new FormatException("Robots file has too many user-agent entries.");
                continue;
            }

            if (group.UserAgents.Count == 0) continue;
            if (key.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                hasDirectives = true;
                if (value.Length == 0) continue;
                if (value.Length > MaximumRuleLength) throw new FormatException("Robots rule exceeds the supported limit.");
                if (!value.StartsWith('/')) continue;
                group.Rules.Add(new Rule(value, key.Equals("allow", StringComparison.OrdinalIgnoreCase)));
                if (++totalRules > MaximumRules) throw new FormatException("Robots file has too many rules.");
            }
            else if (key.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase))
            {
                if (!decimal.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
                    throw new FormatException("Robots crawl delay is malformed.");
                var maximumMilliseconds = CollectionProtocol.MaximumCrawlDelayMilliseconds;
                if (seconds > maximumMilliseconds / 1000m)
                    throw new FormatException("Robots crawl delay exceeds the supported range.");
                var milliseconds = decimal.Ceiling(seconds * 1000m);
                if (milliseconds > maximumMilliseconds)
                    throw new FormatException("Robots crawl delay exceeds the supported range.");
                group.CrawlDelayMilliseconds = Math.Max(group.CrawlDelayMilliseconds ?? 0, (long)milliseconds);
            }
        }
        FinishGroup();

        // RFC 9309 combines groups with the matching product token. The wildcard
        // group applies only when there is no matching product-specific group.
        var matching = groups.Any(g => g.UserAgents.Any(IsCivicLensAgent))
            ? groups.Where(g => g.UserAgents.Any(IsCivicLensAgent)).ToArray()
            : groups.Where(g => g.UserAgents.Any(a => a == "*")).ToArray();
        var selectedRules = matching.SelectMany(g => g.Rules).ToList();
        if (selectedRules.Count > MaximumRules) throw new FormatException("Robots file has too many rules.");
        var delay = matching.Select(g => g.CrawlDelayMilliseconds).Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();
        return new RobotsRules(selectedRules, matching.Any(g => g.CrawlDelayMilliseconds.HasValue) ? delay : null);

        void FinishGroup()
        {
            if (group.UserAgents.Count > 0)
            {
                if (groups.Count >= MaximumGroups) throw new FormatException("Robots file has too many groups.");
                groups.Add(group);
            }
            group = new Group();
            hasDirectives = false;
        }
    }

    private static bool IsCivicLensAgent(string userAgent) =>
        userAgent.Equals("CivicLens", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUnreserved(string value, bool pattern = true)
    {
        value = Regex.Replace(value, "[^\\x00-\\x7F]+", match => Uri.EscapeDataString(match.Value),
            RegexOptions.CultureInvariant, MatchTimeout);
        var output = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if ((value[i] == '*' && !pattern) || (value[i] == '$' && (!pattern || i != value.Length - 1)))
            {
                output.Append(value[i] == '*' ? "%2A" : "%24");
                continue;
            }
            if (value[i] != '%' || i + 2 >= value.Length ||
                !byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var decoded))
            {
                output.Append(value[i]);
                continue;
            }
            var character = (char)decoded;
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~')
                output.Append(character);
            else
                output.Append('%').Append(char.ToUpperInvariant(value[i + 1])).Append(char.ToUpperInvariant(value[i + 2]));
            i += 2;
        }
        return output.ToString();
    }

    private static bool RuleMatches(string rule, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rule = NormalizeUnreserved(rule);
        var terminal = rule.EndsWith('$');
        if (terminal) rule = rule[..^1];
        var expression = "^" + Regex.Escape(rule).Replace("\\*", ".*") + (terminal ? "$" : ".*");
        return Regex.IsMatch(path, expression, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);
    }

    private sealed class Group
    {
        public List<string> UserAgents { get; } = [];
        public List<Rule> Rules { get; } = [];
        public long? CrawlDelayMilliseconds { get; set; }
    }

    private sealed record Rule(string Path, bool Allow)
    {
        public int Specificity
        {
            get
            {
                var normalized = NormalizeUnreserved(Path);
                var octets = 0;
                for (var i = 0; i < normalized.Length; i++)
                {
                    if (normalized[i] == '*' || normalized[i] == '$') continue;
                    octets++;
                    if (normalized[i] == '%' && i + 2 < normalized.Length &&
                        byte.TryParse(normalized.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out _)) i += 2;
                }
                return octets;
            }
        }
    }
}
