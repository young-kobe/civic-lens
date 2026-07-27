import { Card } from '../components/common';
import { COLORS } from '../theme';
import { DigestSection } from './home/DigestSection';

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
    // All interactive styling (hover/focus/active) lives in the
    // `.tab-card` CSS class so keyboard users and touch users get the
    // same affordances as mouse users. Previously the hover state was
    // applied inline via onMouseEnter, which never fires for
    // keyboard-focus or touch, so tab navigation produced no visual
    // feedback.
    return (
        <button
            type="button"
            onClick={() => onClick(tabId)}
            className="tab-card"
        >
            <div className="eyebrow tab-card-tagline">{tagline}</div>
            <div className="tab-card-title">{title}</div>
            <div className="tab-card-body">{body}</div>
            <div className="eyebrow tab-card-cta">Open &rarr;</div>
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
                    See the shape of political media.
                </h2>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-600)',
                    maxWidth: 680,
                }}>
                    Most political coverage tells you how to feel about a story. Civic Lens
                    does something different. We pull US political content from news sites,
                    Reddit, and X, then score each post for sentiment, propaganda techniques,
                    automation signals, and the claims it repeats. Every number here links
                    back to the raw text it came from, with a confidence score attached. It
                    is a sample of what we collected. It is not a poll.
                </p>
            </section>

            {/* Live digest — this week's data, straight from the tabs. */}
            <DigestSection />

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
                    across stories: which claims keep repeating, where we first saw them in our sample,
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
                    <li>Which political <strong>claims</strong> are repeating across sources, and how large each story's sample is.</li>
                    <li>How <strong>news tone</strong> compares to <strong>social media tone</strong> on the same political stories.</li>
                    <li>Which accounts in political discussion look <strong>automated</strong>, and what signals flagged them.</li>
                    <li>Which <strong>propaganda techniques</strong> show up in political content, quoted verbatim from the source.</li>
                    <li>Each tracked entity's <strong>political lean</strong> — stated fact for officials, curated editorial rating for outlets, or an evidence-backed estimate for accounts — never rendered without its source.</li>
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
                        alignItems: 'start',
                    }}
                >
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            01 &middot; Collect
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            We pull US political content from curated news sites, Reddit, and X on
                            a loop. Every raw response is saved by its content hash so any
                            claim on the site can be audited back to what was originally seen.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            02 &middot; Score
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            An AI model reads each collected post and makes four separate judgments:
                        </div>
                        <dl style={{
                            margin: 'var(--space-2) 0 0',
                            fontSize: 'var(--text-sm)',
                            color: 'var(--neutral-700)',
                            lineHeight: 'var(--leading-relaxed)',
                        }}>
                            <div style={{ marginBottom: 'var(--space-2)' }}>
                                <dt style={{ fontWeight: 600, display: 'inline' }}>Tone. </dt>
                                <dd style={{ display: 'inline', margin: 0 }}>
                                    Does the post read positive, negative, or neutral, and about whom.
                                </dd>
                            </div>
                            <div style={{ marginBottom: 'var(--space-2)' }}>
                                <dt style={{ fontWeight: 600, display: 'inline' }}>Persuasion techniques. </dt>
                                <dd style={{ display: 'inline', margin: 0 }}>
                                    Does the wording use tactics like name-calling or appeals to fear —
                                    a read on the writing style, never on whether a claim is true or what
                                    the author intended.
                                </dd>
                            </div>
                            <div style={{ marginBottom: 'var(--space-2)' }}>
                                <dt style={{ fontWeight: 600, display: 'inline' }}>Automation signals. </dt>
                                <dd style={{ display: 'inline', margin: 0 }}>
                                    Does the account's posting behavior look automated — a lead worth
                                    checking, not a verdict.
                                </dd>
                            </div>
                            <div>
                                <dt style={{ fontWeight: 600, display: 'inline' }}>Repeated claims. </dt>
                                <dd style={{ display: 'inline', margin: 0 }}>
                                    Posts making the same claim are grouped into a "story" so you can
                                    watch a claim travel across sources.
                                </dd>
                            </div>
                        </dl>
                        <div style={{ marginTop: 'var(--space-2)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            Every one of these four judgments carries a confidence score shown right
                            in the interface. A sample of them is checked by a human reviewer, and
                            when too few posts back a number, we withhold it rather than guess.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: COLORS.accent, fontWeight: 600 }}>
                            03 &middot; Serve
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)', lineHeight: 'var(--leading-relaxed)' }}>
                            Every panel aggregates the database live, on request — there is no
                            cache to go stale. The freshness signal you see is the ingestion
                            pipeline's own last recorded run, not a snapshot timestamp.
                        </div>
                    </div>
                </div>
                <p style={{
                    marginTop: 'var(--space-4)',
                    marginBottom: 0,
                    fontSize: 'var(--text-sm)',
                    color: 'var(--neutral-600)',
                    lineHeight: 'var(--leading-relaxed)',
                    maxWidth: 820,
                }}>
                    <strong>What this isn't:</strong> the posts we score are a sample of what we
                    collected, not a poll of the public. A flag describes the wording or behavior
                    in a post we saw — it is never a verdict on the person who wrote it.
                </p>
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
                        body="Net tone of the political content we sampled, split three ways: news outlets, verified officials and collectives, and communities. Each entity gets its own card with a political-lean label — official party, curated media lean, or a derived estimate with its evidence."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="narratives"
                        tagline="Tab &middot; Political Narratives"
                        title="Political claims repeating across sources"
                        body="Each row is a political claim we saw repeated across multiple articles or posts, ranked by how many posts carry it. Claims are also flagged when a large share of their posts contain propaganda techniques or come from likely-automated accounts."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="propaganda"
                        tagline="Tab &middot; Propaganda"
                        title="Propaganda techniques flagged in political content"
                        body="An AI pass scans each article or post for six specific techniques (loaded language, name-calling, ad hominem, appeal to fear, whataboutism, doubt casting). It has to quote a verbatim phrase from the source as evidence. You get a technique breakdown, a news vs social split, and examples with the quoted evidence attached."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="bots"
                        tagline="Tab &middot; Bot Detector"
                        title="Accounts in political discourse that look automated"
                        body="Behavioral signals (posting rate, text repetition, account age, coordinated timing) scored per account across our political content sample to flag likely automation. Flagged content is excluded from sentiment aggregates by default. These are leads, not verdicts."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="desk"
                        tagline="Tab &middot; Data Desk"
                        title="Every signal, side by side"
                        body="The numbers-forward view: a sortable matrix joining every tracked entity's tone and bot-detection rate, the full movers board, small-multiple story trend charts, and the pipeline's own health and human-agreement readouts."
                        onClick={onNavigate}
                    />
                    {isAdmin && (
                        <TabCard
                            tabId="review"
                            tagline="Tab &middot; Review (admin)"
                            title="Human quality check on AI outputs"
                            body="Internal queue for marking political-content classifications correct or incorrect. Reviewed rows can be flagged as reference examples used to calibrate confidence and track accuracy over time."
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
                        carries a score, and each quote is verified as a verbatim substring of the source.
                    </li>
                    <li>
                        <strong>Samples are labeled as samples.</strong> &ldquo;Overall Tone&rdquo; means the
                        tone of the posts we collected, not the tone of the public. Reach and influence
                        numbers are estimates, and are labeled as estimates when we can't verify real audience size.
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
                    Civic Lens is built by an independent developer. Every output is
                    traceable, confidence-scored, and labeled as a sample. Civic Lens does not claim to measure
                    national sentiment, public opinion, or causal propagation. It shows you the shape of the data
                    we collected, with the methodology visible and the evidence attached.
                </p>
            </section>
        </div>
    );
}

export default Home;
