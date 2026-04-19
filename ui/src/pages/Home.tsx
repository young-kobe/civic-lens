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
            {/* Hero */}
            <section
                style={{
                    padding: 'var(--space-6) var(--space-5)',
                    background: 'var(--bg-card)',
                    border: '1px solid var(--neutral-200)',
                    borderRadius: 'var(--radius-md)',
                }}
            >
                <div className="eyebrow" style={{ color: 'var(--accent)' }}>
                    Political media analysis · narrative &amp; bot tracker
                </div>
                <h2
                    style={{
                        margin: 'var(--space-2) 0 var(--space-3)',
                        fontSize: 'var(--text-3xl)',
                        letterSpacing: '-0.02em',
                        lineHeight: 'var(--leading-tight)',
                    }}
                >
                    What US politics looks like across the sources we sample.
                </h2>
                <p style={{
                    margin: 0,
                    fontSize: 'var(--text-base)',
                    lineHeight: 'var(--leading-relaxed)',
                    color: 'var(--neutral-600)',
                    maxWidth: 720,
                }}>
                    Civic Lens is a <strong>political media analysis tool</strong>. It ingests US-political news
                    articles, Reddit posts from political subreddits, and X posts from political queries, then runs
                    every document through traceable AI analysis — sentiment, bot-likelihood, claim extraction — so
                    you can see how the same <strong>political claims</strong> show up across different sources, who
                    repeats them, and which accounts look automated. It is a <strong>sample</strong>, not a poll;
                    every number on this site is derived from the docs we happen to ingest, with a confidence score
                    and a direct link to the raw text.
                </p>
            </section>

            {/* How it works */}
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
                            01 · Ingest
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            A Go crawler fetches US-political articles from curated RSS feeds, political subreddits via
                            the Reddit API, and political queries via the X API. Every raw response is stored by content
                            hash so it can be audited later.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: 'var(--accent)', fontWeight: 600 }}>
                            02 · Analyze
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            Python engines classify sentiment, score bot-likelihood, extract claims, and cluster repeat
                            claims into narratives. Each output is written with a confidence score and an evidence span
                            taken verbatim from the source text.
                        </div>
                    </div>
                    <div>
                        <div className="num" style={{ fontSize: 'var(--text-sm)', color: 'var(--accent)', fontWeight: 600 }}>
                            03 · Serve
                        </div>
                        <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-sm)', color: 'var(--neutral-700)' }}>
                            Aggregations run once and cache to JSON. The FastAPI server serves the cache to this
                            dashboard. Nothing is computed at page-load — what you see is a snapshot refreshed on a
                            schedule.
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
                        body="Net sentiment of the political content we sampled, broken down by platform (news, Reddit, X), by topic, and over time. Includes a side-by-side comparison of social-media vs news-outlet tone on political stories, and a GOP-favorability card sourced from the same per-doc analysis."
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
                        body="An LLM scans each doc for six specific techniques (loaded language, name-calling, ad hominem, appeal-to-fear, whataboutism, doubt-casting) and must quote a verbatim phrase from the source as evidence. Shows technique breakdown, news-vs-social split, and examples with the quoted evidence."
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
                        version, and the prompt version used to produce it. You can trace any metric back to the raw
                        text.
                    </li>
                    <li>
                        <strong>Confidence is visible.</strong> No claim is presented as fact — every classification
                        carries a score, and evidence spans are checked for being a verbatim substring of the source.
                    </li>
                    <li>
                        <strong>Samples are labeled as samples.</strong> "Public Sentiment" means sentiment of the docs
                        we ingested, not sentiment of the public. Reach and influence metrics are marked as proxies
                        when they're not backed by verified audience data.
                    </li>
                    <li>
                        <strong>We delete rather than approximate.</strong> If a metric can't be computed honestly, we
                        remove it. When a feature is built, it gets a walkthrough document that records what changed
                        and why — see <code>docs/walkthroughs/</code>.
                    </li>
                </ul>
            </Card>

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
