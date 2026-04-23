import { Card } from '../components/common';
import { COLORS } from '../theme';

interface HomeProps {
    onNavigate: (tabId: string) => void;
    isAdmin?: boolean;
}

interface TabCardProps {
    tabId: string;
    title: string;
    tagline: string;
    body: string;
    onClick: (tabId: string) => void;
}

function TabCard({ tabId, title, tagline, body, onClick }: TabCardProps) {
    return (
        <button
            type="button"
            onClick={() => onClick(tabId)}
            className="tab-card"
            style={{
                textAlign: 'left',
                background: 'var(--bg-card)',
                border: '1px solid var(--neutral-200)',
                borderRadius: 'var(--radius-md)',
                padding: 'var(--space-4) var(--space-5)',
                cursor: 'pointer',
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--space-2)',
                width: '100%',
                fontFamily: 'inherit',
                color: 'inherit',
                transition: 'border-color 180ms ease, box-shadow 180ms ease, transform 180ms ease',
            }}
            onMouseEnter={(e) => {
                e.currentTarget.style.borderColor = COLORS.accent;
                e.currentTarget.style.boxShadow = 'var(--shadow-md)';
                e.currentTarget.style.transform = 'translateY(-1px)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.borderColor = 'var(--neutral-200)';
                e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
                e.currentTarget.style.transform = 'translateY(0)';
            }}
        >
            <div className="eyebrow" style={{ color: COLORS.accent }}>{tagline}</div>
            <div style={{ fontSize: 'var(--text-lg)', fontWeight: 600, letterSpacing: '-0.01em' }}>{title}</div>
            <div style={{ fontSize: 'var(--text-sm)', color: 'var(--neutral-600)', lineHeight: 'var(--leading-normal)' }}>
                {body}
            </div>
            <div
                className="eyebrow"
                style={{ color: 'var(--neutral-500)', marginTop: 'var(--space-1)' }}
            >
                Open &rarr;
            </div>
        </button>
    );
}

function Home({ onNavigate, isAdmin = false }: HomeProps) {
    return (
        <div className="flex flex-col gap-6" style={{ maxWidth: 980, margin: '0 auto' }}>
            {/* Hero */}
            <section className="surface-hero">
                <div className="eyebrow" style={{ color: COLORS.accent }}>
                    Political media analysis &middot; narrative &amp; bot tracker
                </div>
                <h2
                    className="headline-display"
                    style={{
                        margin: 'var(--space-2) 0 var(--space-3)',
                        fontSize: 'var(--text-3xl)',
                        lineHeight: 'var(--leading-tight)',
                        maxWidth: 720,
                    }}
                >
                    See the shape of political media, unbiased.
                </h2>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-600)',
                    maxWidth: 680,
                }}>
                    Most political coverage tells you how to feel about a story. Civic Lens
                    does something different. We pull US political content from news sites
                    and X, then score each post for sentiment, propaganda techniques,
                    automation signals, and the claims it repeats. Every number here links
                    back to the raw text it came from, with a confidence score attached. It
                    is a sample of what we collected. It is not a poll.
                </p>
            </section>

            {/* Why it exists */}
            <section className="surface-panel">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>Why Civic Lens exists</div>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                    maxWidth: 820,
                }}>
                    News sites tell you what each story says. They rarely show you the pattern
                    across stories: which claims keep repeating, where they were seen first,
                    who is amplifying them, how the tone differs between newsrooms and online
                    crowds. That pattern is measurable. You should not have to take a single
                    source&apos;s framing on faith when the raw data is sitting right there.
                </p>
            </section>

            {/* What it tells you */}
            <section className="surface-card">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-3)' }}>What Civic Lens tells you</div>
                <ul style={{
                    margin: 0,
                    paddingLeft: 'var(--space-5)',
                    fontSize: 'var(--text-sm)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                }}>
                    <li>Which political <strong>claims</strong> are repeating across sources, and which source surfaced them first in our sample.</li>
                    <li>How <strong>news tone</strong> compares to <strong>social media tone</strong> on the same political stories.</li>
                    <li>Which accounts in political discussion look <strong>automated</strong>, and what signals flagged them.</li>
                    <li>Which <strong>propaganda techniques</strong> show up in political content, quoted verbatim from the source.</li>
                    <li><strong>Favorability</strong> scores per political figure, pulled from the same per-document analysis.</li>
                    <li>Every one of these is broken down by source, topic, and time window.</li>
                </ul>
            </section>

            {/* How it works */}
            <section className="surface-panel">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-3)' }}>How it works</div>
                <div
                    style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                        gap: 'var(--space-4)',
                    }}
                >
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            01 &middot; Collect
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            We pull US political content from curated news sites and X on
                            a loop. Every raw response is saved by its content hash so any
                            claim on the site can be audited back to what was originally seen.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            02 &middot; Score
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            Each document is scored for sentiment, bot likelihood,
                            propaganda techniques, claim extraction, and narrative clustering.
                            Every score carries a confidence value and a verbatim evidence
                            span taken from the source.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            03 &middot; Serve
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            Aggregations run on a schedule and cache to snapshots. The
                            dashboard reads those snapshots directly. Nothing is computed at
                            page load. What you see is a timestamped picture.
                        </div>
                    </div>
                </div>
            </section>

            {/* Tabs */}
            <section>
                <div className="eyebrow" style={{ marginBottom: 'var(--space-3)' }}>What each tab shows</div>
                <div
                    style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
                        gap: 'var(--space-3)',
                    }}
                >
                    <TabCard
                        tabId="sentiment"
                        tagline="Tab &middot; Overall Tone"
                        title="How sources are talking about politics"
                        body="Net tone of the political content we sampled, split three ways: news outlets, verified officials, and the general public. Each entity gets its own card with a partisan-lean chip and an editorial blurb. A divergence panel shows where the three tiers disagree most on each topic."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="narratives"
                        tagline="Tab &middot; Political Narratives"
                        title="Political claims repeating across sources"
                        body="Each row is a political claim we saw in more than one doc, bucketed by where we first saw it: news outlets, verified officials, or the general public. Cross-tier narratives — claims that show up in more than one tier — get a dedicated panel. Amplification overlays flag claims with high propaganda density or a bot-pushed fraction."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="propaganda"
                        tagline="Tab &middot; Propaganda"
                        title="Propaganda techniques flagged in political content"
                        body="An AI pass scans each doc for six specific techniques (loaded language, name-calling, ad hominem, appeal to fear, whataboutism, doubt casting). It has to quote a verbatim phrase from the source as evidence. You get a technique breakdown, a news vs social split, and examples with the quoted evidence attached."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="bots"
                        tagline="Tab &middot; Bot Detector"
                        title="Accounts in political discourse that look automated"
                        body="Behavioral signals (posting rate, text repetition, account age, coordinated timing) scored per account across our political content sample to flag likely automation. Flagged content is excluded from sentiment aggregates by default. These are leads, not verdicts."
                        onClick={onNavigate}
                    />
                    {isAdmin && (
                        <TabCard
                            tabId="review"
                            tagline="Tab &middot; Review (admin)"
                            title="Human quality check on AI outputs"
                            body="Internal queue for marking political-content classifications correct or incorrect. Reviewed rows can be flagged as golden, which is what we use to calibrate confidence and track accuracy over time."
                            onClick={onNavigate}
                        />
                    )}
                </div>
            </section>

            {/* Principles */}
            <Card title="How we keep ourselves honest">
                <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)', lineHeight: 'var(--leading-relaxed)' }}>
                    <li>
                        <strong>Every number links to a source.</strong> Each AI output stores the doc id, the
                        model version, and the prompt version used to produce it. Any metric on this site traces
                        back to the raw text it came from.
                    </li>
                    <li>
                        <strong>Confidence is visible.</strong> No classification is presented as fact. Each one
                        carries a score, and evidence spans are verified as a verbatim substring of the source.
                    </li>
                    <li>
                        <strong>Samples are labeled as samples.</strong> &ldquo;Overall Tone&rdquo; means the
                        tone of the docs we ingested, not the tone of the public. Reach and influence
                        numbers are marked as proxies when they are not backed by verified audience data.
                    </li>
                    <li>
                        <strong>We delete rather than approximate.</strong> If a metric cannot be computed
                        honestly, we remove it. Every change is recorded in an internal change log so the audit
                        trail stays intact.
                    </li>
                </ul>
            </Card>

            {/* Who built it */}
            <section className="surface-panel">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>Who built Civic Lens</div>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-sm)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                    maxWidth: 820,
                }}>
                    Civic Lens is built by <strong>Kobe Young</strong>, an independent developer. Every output is
                    traceable, confidence-scored, and labeled as a sample. Civic Lens does not claim to measure
                    national sentiment, public opinion, or causal propagation. It shows you the shape of the data
                    we collected, with the methodology visible and the evidence attached.
                </p>
            </section>
        </div>
    );
}

export default Home;
