import { Card } from '../components/common';

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
                transition: 'border-color 120ms ease, transform 120ms ease',
            }}
            onMouseEnter={(e) => {
                e.currentTarget.style.borderColor = 'var(--accent)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.borderColor = 'var(--neutral-200)';
            }}
        >
            <div className="eyebrow" style={{ color: 'var(--accent)' }}>{tagline}</div>
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
            {/* Hero — what is Civic Lens */}
            <section
                style={{
                    padding: 'var(--space-6) var(--space-5)',
                    background: 'var(--bg-card)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ color: 'var(--accent)' }}>
                    Political media analysis &middot; narrative &amp; bot tracker
                </div>
                <h2
                    style={{
                        margin: 'var(--space-2) 0 var(--space-3)',
                        fontSize: 'var(--text-3xl)',
                        letterSpacing: '-0.02em',
                        lineHeight: 'var(--leading-tight)',
                    }}
                >
                    The shape of political media, measured &mdash; not framed.
                </h2>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-600)',
                    maxWidth: 720,
                }}>
                    Most of what you hear about politics arrives pre-framed. Civic Lens is the
                    opposite: it <strong>continuously samples US-political content across news,
                    public social discussions, and public-posting platforms</strong>, then scores
                    every single document for sentiment, bot-likelihood, propaganda techniques,
                    and narrative membership. Every number on this site comes with a confidence
                    score and a direct link to the raw text it was drawn from. It is a{' '}
                    <strong>sample</strong>, not a poll &mdash; we show you the shape of the data
                    we collected, never more than that.
                </p>
            </section>

            {/* Why Civic Lens exists */}
            <section
                style={{
                    padding: 'var(--space-5)',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>Why Civic Lens exists</div>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                    maxWidth: 820,
                }}>
                    Political coverage tells you what to think about a story. It rarely shows you
                    the pattern across stories &mdash; which claims are repeating, where they were
                    seen first, which voices are amplifying them, how the tone differs between
                    newsrooms and crowds. Civic Lens was built because that pattern is measurable,
                    and because no reader should have to trust a single source&apos;s framing when
                    the raw data is right there.
                </p>
            </section>

            {/* What Civic Lens tells you */}
            <section
                style={{
                    padding: 'var(--space-5)',
                    background: 'var(--bg-card)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-3)' }}>What Civic Lens tells you</div>
                <ul style={{
                    margin: 0,
                    paddingLeft: 'var(--space-5)',
                    fontSize: 'var(--text-sm)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                }}>
                    <li>Which political <strong>claims</strong> are being repeated across sources, and which source surfaces them first in our sample</li>
                    <li>How <strong>news-outlet tone</strong> compares to <strong>social-media tone</strong> on the same political stories</li>
                    <li>Which accounts in political discussion look <strong>automated</strong> &mdash; and why</li>
                    <li>Which <strong>propaganda techniques</strong> show up in political content, quoted verbatim from the source</li>
                    <li><strong>Favorability</strong> scores per political figure, derived from the same per-document analysis</li>
                    <li>Every one of these is broken down by source, topic, and time window</li>
                </ul>
            </section>

            {/* How it works — sanitized */}
            <section
                style={{
                    padding: 'var(--space-5)',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-3)' }}>How it works</div>
                <div
                    style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                        gap: 'var(--space-4)',
                    }}
                >
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: 'var(--accent)', fontWeight: 600 }}>
                            01 &middot; Ingest
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            Civic Lens continuously samples US-political content from curated news,
                            social, and public-posting sources. Every raw response is stored by
                            content hash so any claim on the site can be audited back to what was
                            originally seen.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: 'var(--accent)', fontWeight: 600 }}>
                            02 &middot; Analyze
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            Each document is run through a traceable AI pipeline &mdash; sentiment,
                            bot-likelihood, claim extraction, propaganda detection, narrative
                            clustering. Each output carries a confidence score and a verbatim
                            evidence span taken directly from the source text.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: 'var(--accent)', fontWeight: 600 }}>
                            03 &middot; Serve
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            Aggregations run on a schedule and cache to snapshots. The dashboard
                            reads those snapshots directly. Nothing is computed at page-load
                            &mdash; what you see is a timestamped picture, not a live recompute.
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
                        tagline="Tab · Public Sentiment"
                        title="How sources are talking about politics"
                        body="Net sentiment of the political content we sampled, broken down by platform, by topic, and over time. Includes a side-by-side comparison of social-media vs news-outlet tone on political stories, and a GOP-favorability card sourced from the same per-doc analysis."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="narratives"
                        tagline="Tab · Narratives"
                        title="Political claims being repeated across sources"
                        body="Each row is a political claim we saw in more than one doc — grouped by where we first saw it (news vs social). You see how many docs repeat the claim, the source mix, the daily volume, net sentiment of the supporting docs, and how many of the cited sources are also in our sample."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="propaganda"
                        tagline="Tab · Propaganda"
                        title="Propaganda techniques flagged in political content"
                        body="An AI pipeline scans each doc for six specific techniques (loaded language, name-calling, ad hominem, appeal-to-fear, whataboutism, doubt-casting) and must quote a verbatim phrase from the source as evidence. Shows technique breakdown, news-vs-social split, and examples with the quoted evidence."
                        onClick={onNavigate}
                    />
                    <TabCard
                        tabId="bots"
                        tagline="Tab · Bot Detector"
                        title="Accounts in political discourse that look automated"
                        body="Behavioral signals — posting rate, text repetition, account age, coordinated timing — scored per account across our political-content sample to flag likely automation. Flagged content is excluded from sentiment aggregates by default. These are leads, not verdicts."
                        onClick={onNavigate}
                    />
                    {isAdmin && (
                        <TabCard
                            tabId="review"
                            tagline="Tab · Review (admin)"
                            title="Human quality check on AI outputs"
                            body="Internal queue for marking the AI's political-content classifications correct or incorrect. Reviewed rows can be flagged as golden — they're what we use to calibrate confidence and track accuracy over time."
                            onClick={onNavigate}
                        />
                    )}
                </div>
            </section>

            {/* Principles */}
            <Card title="How we keep ourselves honest">
                <ul style={{ margin: 0, paddingLeft: 'var(--space-5)', fontSize: 'var(--text-sm)', lineHeight: 'var(--leading-relaxed)' }}>
                    <li>
                        <strong>Every number links to a source.</strong> Each AI output stores the doc id, the model
                        version, and the prompt version used to produce it. Any metric on this site traces back to
                        the raw text it came from.
                    </li>
                    <li>
                        <strong>Confidence is visible.</strong> No claim is presented as fact &mdash; every
                        classification carries a score, and evidence spans are verified as a verbatim substring of
                        the source.
                    </li>
                    <li>
                        <strong>Samples are labeled as samples.</strong> &ldquo;Public Sentiment&rdquo; means
                        sentiment of the docs we ingested, not sentiment of the public. Reach and influence metrics
                        are marked as proxies when they are not backed by verified audience data.
                    </li>
                    <li>
                        <strong>We delete rather than approximate.</strong> If a metric cannot be computed honestly,
                        we remove it. Every change to the project is recorded in an internal change-log so the audit
                        trail stays intact.
                    </li>
                </ul>
            </Card>

            {/* Who built Civic Lens */}
            <section
                style={{
                    padding: 'var(--space-5)',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ marginBottom: 'var(--space-2)' }}>Who built Civic Lens</div>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-sm)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-700)',
                    maxWidth: 820,
                }}>
                    Civic Lens is built by <strong>Kobe Young</strong>, an independent developer.
                    It is an audit-first project &mdash; every output is traceable,
                    confidence-scored, and labeled as a sample. Civic Lens does not claim to
                    measure national sentiment, public opinion, or causal propagation; it shows
                    you the shape of the data we collected, with the methodology visible and the
                    evidence attached.
                </p>
            </section>

            {/* Footer */}
            <div
                style={{
                    fontSize: 'var(--text-xs)',
                    color: 'var(--neutral-500)',
                    fontFamily: 'var(--font-mono)',
                    letterSpacing: '0.04em',
                    textAlign: 'center',
                    padding: 'var(--space-3) 0',
                }}
            >
                <span>Click any tab above to start, or return here anytime by clicking the CIVIC LENS title.</span>
            </div>
        </div>
    );
}

export default Home;
