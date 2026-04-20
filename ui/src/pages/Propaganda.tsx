import { Card, MetricCard, MethodPopover, LoadingCard, EmptyState, ErrorState } from '../components/common';
import { fetchPropaganda } from '../services/api';
import { useFetch } from '../services/useFetch';
import type {
    Filters, PropagandaOverview, PropagandaTechniqueCount, PropagandaSourceSplit,
    PropagandaExample, PropagandaTechniqueName,
} from '../types';

const TECHNIQUE_LABEL: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Loaded Language',
    name_calling: 'Name Calling',
    ad_hominem: 'Ad Hominem',
    appeal_to_fear: 'Appeal to Fear',
    whataboutism: 'Whataboutism',
    doubt_casting: 'Doubt-Casting',
};

const TECHNIQUE_BLURB: Record<PropagandaTechniqueName, string> = {
    loaded_language: 'Emotionally charged framing designed to influence rather than inform.',
    name_calling: 'Dismissive labels applied to a person or group instead of engaging their argument.',
    ad_hominem: 'Attacks on the speaker\'s character rather than the substance of what they said.',
    appeal_to_fear: 'Fear or catastrophic imagery used to bypass reasoning.',
    whataboutism: 'Deflecting criticism by pointing at an unrelated alleged misconduct by the other side.',
    doubt_casting: 'Insinuation of wrongdoing or unreliability without offering evidence.',
};


interface TechniqueRowProps {
    technique: PropagandaTechniqueCount;
    maxCount: number;
}

function TechniqueRow({ technique, maxCount }: TechniqueRowProps) {
    const name = technique.technique as PropagandaTechniqueName;
    const label = TECHNIQUE_LABEL[name] || name;
    const blurb = TECHNIQUE_BLURB[name] || '';
    const widthPct = maxCount > 0 ? (technique.count / maxCount) * 100 : 0;
    return (
        <div
            style={{
                display: 'grid',
                gridTemplateColumns: '180px 1fr 60px',
                gap: 'var(--space-3)',
                alignItems: 'center',
                padding: 'var(--space-2) var(--space-4)',
                borderBottom: '1px solid var(--neutral-150)',
            }}
        >
            <div>
                <div style={{ fontWeight: 600, fontSize: 'var(--text-sm)' }}>{label}</div>
                <div className="eyebrow" style={{ color: 'var(--neutral-500)', marginTop: 2 }}>
                    {blurb}
                </div>
            </div>
            <div style={{ position: 'relative', height: 16, background: 'var(--neutral-100)', borderRadius: 2 }}>
                <div
                    style={{
                        position: 'absolute',
                        top: 0, left: 0, bottom: 0,
                        width: `${widthPct}%`,
                        background: '#b91c1c',
                        borderRadius: 2,
                    }}
                />
            </div>
            <div className="num" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                {technique.count.toLocaleString()}
                <span className="eyebrow" style={{ display: 'block', color: 'var(--neutral-500)' }}>
                    {technique.pct_of_flagged_docs.toFixed(0)}% of flagged
                </span>
            </div>
        </div>
    );
}


interface SourceSplitRowProps {
    split: PropagandaSourceSplit;
}

function SourceSplitRow({ split }: SourceSplitRowProps) {
    return (
        <div
            style={{
                display: 'grid',
                gridTemplateColumns: '1fr 80px 90px 80px',
                gap: 'var(--space-4)',
                padding: 'var(--space-3) var(--space-4)',
                borderBottom: '1px solid var(--neutral-150)',
                alignItems: 'center',
            }}
        >
            <div style={{ fontWeight: 600 }}>{split.label}</div>
            <div className="num" style={{ textAlign: 'right', color: 'var(--neutral-600)' }}>
                {split.total_docs.toLocaleString()}
            </div>
            <div className="num" style={{ textAlign: 'right', fontWeight: 600 }}>
                {split.flagged_rate_pct.toFixed(1)}%
            </div>
            <div className="num" style={{ textAlign: 'right', color: 'var(--neutral-600)' }}>
                {split.mean_score.toFixed(2)}
            </div>
        </div>
    );
}


interface ExampleCardProps {
    example: PropagandaExample;
}

function ExampleCard({ example }: ExampleCardProps) {
    return (
        <div
            style={{
                padding: 'var(--space-3) var(--space-4)',
                borderBottom: '1px solid var(--neutral-150)',
            }}
        >
            <div className="flex items-baseline justify-between mb-2">
                <div className="eyebrow" style={{ color: 'var(--neutral-500)' }}>
                    {example.source_type} · {example.domain || 'unknown domain'} · doc #{example.doc_id}
                </div>
                <div className="num" style={{ fontWeight: 600, color: '#b91c1c' }}>
                    score {example.overall_score.toFixed(2)}
                </div>
            </div>
            {example.title && (
                <div style={{ fontWeight: 600, fontSize: 'var(--text-sm)', marginBottom: 4 }}>
                    {example.title}
                </div>
            )}
            <div style={{ fontSize: 'var(--text-xs)', color: 'var(--neutral-600)', marginBottom: 'var(--space-2)' }}>
                {example.text_preview}
                {example.text_preview.length >= 240 ? '…' : ''}
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-2)' }}>
                {example.techniques.map((t, i) => (
                    <div
                        key={i}
                        style={{
                            padding: '2px var(--space-2)',
                            background: '#fef2f2',
                            border: '1px solid #fecaca',
                            borderRadius: 4,
                            fontSize: 'var(--text-xs)',
                        }}
                        title={`confidence ${t.confidence.toFixed(2)}`}
                    >
                        <strong>{TECHNIQUE_LABEL[t.technique as PropagandaTechniqueName] || t.technique}</strong>
                        {' — '}
                        <em>"{t.evidence_span}"</em>
                    </div>
                ))}
            </div>
        </div>
    );
}


interface PropagandaProps {
    filters: Filters;
}

function Propaganda({ filters }: PropagandaProps) {
    const { data, loading, error, refetch } = useFetch<PropagandaOverview>(
        () => fetchPropaganda(filters.timeRange),
        [filters.timeRange],
        `propaganda:${filters.timeRange}`,
    );

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-4">
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }
    if (!data || data.total_eligible_docs === 0) {
        return (
            <EmptyState
                title="No propaganda-scored docs yet"
                description="Run the analysis pipeline (propaganda task) with the LLM backend enabled to populate this view."
            />
        );
    }

    const maxTechniqueCount = data.by_technique.reduce((m, t) => Math.max(m, t.count), 0);

    return (
        <div className="flex flex-col gap-4">
            {/* Plain-language disclaimer */}
            <div
                style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: '#fffbeb',
                    border: '1px solid #fbbf24',
                    borderRadius: 'var(--radius-md)',
                    fontSize: 'var(--text-xs)',
                    color: '#92400e',
                }}
            >
                <strong>How to read this:</strong> Each doc is scored per-technique by an LLM, and only flagged when
                the model provides a verbatim evidence quote from the source text. "Flagged rate" is the % of scored
                docs the model flagged for one or more techniques. High propaganda rates do not imply intent — they
                show how often the sample leaned on these specific techniques. {data.disclaimer}
            </div>

            {/* Overview metrics */}
            <div className="grid-3">
                <MetricCard
                    label="Flagged Rate"
                    value={`${data.propaganda_rate_pct.toFixed(1)}%`}
                    subtitle={`${data.flagged_docs.toLocaleString()} of ${data.total_eligible_docs.toLocaleString()} scored docs (${data.window})`}
                />
                <MetricCard
                    label="Mean Propaganda Score"
                    value={data.mean_score.toFixed(2)}
                    subtitle="Averaged across all scored docs (0 - 1 scale)"
                />
                <MetricCard
                    label="Window"
                    value={data.window}
                    subtitle={`${data.total_eligible_docs.toLocaleString()} eligible docs`}
                />
            </div>

            {/* Technique breakdown */}
            <Card
                title="Techniques Used"
                subtitle="Across flagged docs. A doc can contribute to multiple techniques."
                headerActions={
                    <MethodPopover
                        description="Each technique count is the number of flagged docs where the model identified that technique with a verbatim evidence span."
                        limitations={[
                            'Techniques are detected by an LLM. An empty or unvalidated response drops the technique rather than hallucinating an evidence quote.',
                            'Starter taxonomy covers six techniques; expansion is a schema-compatible future change.',
                        ]}
                    />
                }
            >
                {data.by_technique.map((t) => (
                    <TechniqueRow key={t.technique} technique={t} maxCount={maxTechniqueCount} />
                ))}
            </Card>

            {/* Source split */}
            <Card title="News vs. Social Media" subtitle="Flagged rate and mean score by source bucket">
                <div
                    style={{
                        display: 'grid',
                        gridTemplateColumns: '1fr 80px 90px 80px',
                        gap: 'var(--space-4)',
                        padding: 'var(--space-2) var(--space-4)',
                        borderBottom: '1px solid var(--neutral-200)',
                    }}
                    className="eyebrow"
                >
                    <span>Source</span>
                    <span style={{ textAlign: 'right' }}>Docs</span>
                    <span style={{ textAlign: 'right' }}>Flagged %</span>
                    <span style={{ textAlign: 'right' }}>Mean Score</span>
                </div>
                {data.by_source.map((s) => (
                    <SourceSplitRow key={s.label} split={s} />
                ))}
            </Card>

            {/* Examples */}
            <Card
                title="Recent Flagged Examples"
                subtitle="Most-recent docs above the flag threshold, with verbatim evidence spans"
            >
                {data.examples.length === 0 ? (
                    <div className="eyebrow" style={{ padding: 'var(--space-4)', color: 'var(--neutral-500)' }}>
                        No flagged examples in this window.
                    </div>
                ) : (
                    data.examples.map((ex) => <ExampleCard key={ex.doc_id} example={ex} />)
                )}
            </Card>
        </div>
    );
}

export default Propaganda;
