import { useState } from 'react';
import {
    Card, CollapsibleInfo, DefinitionChip, EmptyState, EntityHeader, ErrorState,
    GlobalTicker, LoadingCard, MethodPopover, Modal, RangeCaption,
    RankedEntityList, SampleCardList, ThreeWayColumn, TwoWayGrid,
} from '../components/common';
import type { ColumnSorter, RankedEntity, TickerItem } from '../components/common';
import { deepLinkHref } from '../services/deepLink';
import { fetchBotActivity, fetchSnapshotStatus } from '../services/api';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { coordinationLevel } from '../services/glossary';
import { COLORS } from '../theme';
import { CoordinationEvidencePanel } from './bots/CoordinationEvidencePanel';
import type {
    AccountAgeBucket, BehavioralSignalBucket, BotActivityResponse, BotPushedNarrative,
    EntityBotRate, Filters, FlaggedAccount, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note, updated for this restore: the pre-redesign       //
//  coordination index, top amplified domain clusters, posting-cadence        //
//  heatmap, copy-paste-similarity distribution, and per-narrative "why        //
//  flagged" LLM breakdown still have no equivalent in the strictly-live       //
//  /bot-activity response (see analysis/src/api/models/bots.py). This        //
//  restore keeps the pre-cutover page's geometry (coordination summary,       //
//  chain-of-evidence strip, per-entity drill-down, 2-up amplification tiles)  //
//  and degrades each to the closest field the response actually carries,     //
//  rather than dropping the panel. Per-panel degradation notes are inline.   //
// --------------------------------------------------------------------------- //

function botRateColor(ratePct: number): string {
    return ratePct > 10 ? COLORS.negative : ratePct > 3 ? COLORS.warning : 'var(--neutral-700)';
}

function readsAsToday(data: BotActivityResponse): string {
    const rate = data.automationRatePct;
    const ratePct = formatPct(rate, { decimals: 0 });
    if (rate > 10) return `A high share of the posts we scanned look automated — roughly ${ratePct}.`;
    if (rate > 3) return `Some of the posts we scanned look automated — about ${ratePct}.`;
    return `Most posts we scanned look like real people — only about ${ratePct} look automated.`;
}

function buildTickerItems(data: BotActivityResponse): TickerItem[] {
    const rate = data.automationRatePct;
    const rateTone = rate > 10 ? 'warning' : rate > 3 ? 'neutral' : 'positive';
    return [
        {
            label: 'Suspected automation',
            value: formatPct(rate, { decimals: 0 }),
            hint: 'of scored posts',
            tone: rateTone as TickerItem['tone'],
            emphasis: true,
            ariaLabel: `Suspected automation rate ${rate.toFixed(1)} percent`,
        },
        { label: 'Analyzed docs', value: data.analyzedDocCount.toLocaleString() },
        { label: 'Bot-scored posts', value: data.botScoredDocCount.toLocaleString() },
    ];
}

// --------------------------------------------------------------------------- //
//  SimilarityBar — generic labeled percentage bar, restored from the          //
//  pre-cutover page's Text Similarity Distribution card. That card's own      //
//  data (pairwise copy-paste similarity buckets) has no equivalent in the     //
//  current response, so the component is reused here for the account-age     //
//  distribution instead of being dropped.                                    //
// --------------------------------------------------------------------------- //

interface SimilarityBarProps {
    label: string;
    value: number;
    color: string;
    title?: string;
}

function SimilarityBar({ label, value, color, title }: SimilarityBarProps) {
    return (
        <div title={title}>
            <div className="flex justify-between text-sm mb-1">
                <span>{label}</span>
                <span className="font-medium" style={{ color }}>{formatPct(value, { decimals: 0 })}</span>
            </div>
            <div style={{ height: '8px', background: 'var(--neutral-100)', overflow: 'hidden' }}>
                <div style={{ width: `${value}%`, height: '100%', background: color }} />
            </div>
        </div>
    );
}

// --------------------------------------------------------------------------- //
//  Account age + behavioral-signal cards                                      //
// --------------------------------------------------------------------------- //

function AccountAgeCard({ buckets }: { buckets: AccountAgeBucket[] }) {
    const total = buckets.reduce((s, b) => s + b.accountCount, 0);
    if (total === 0) return null;
    return (
        <Card
            title="Account age of flagged authors"
            subtitle="Distinct bot-flagged accounts, bucketed by account age. Freshly created accounts skew toward automation."
        >
            <div className="flex flex-col gap-3">
                {buckets.filter((b) => b.accountCount > 0).map((b) => {
                    const pct = (b.accountCount / total) * 100;
                    const isNewest = b.ageRange.startsWith('<');
                    return (
                        <SimilarityBar
                            key={b.ageRange}
                            label={b.ageRange}
                            value={pct}
                            color={isNewest ? COLORS.warning : COLORS.chartAccent}
                            title={`${b.accountCount.toLocaleString()} accounts (${pct.toFixed(0)}%)`}
                        />
                    );
                })}
            </div>
        </Card>
    );
}

function fmtAvg(v: number | null): string {
    return v == null ? '—' : v.toFixed(2);
}

function BehavioralSignalsCard({ buckets }: { buckets: BehavioralSignalBucket[] }) {
    if (buckets.length === 0) return null;
    return (
        <Card
            title="Behavioral signals by label"
            subtitle="Mean typed stylometrics per bot_label bucket — why docs in that bucket read the way they do."
            headerActions={
                <MethodPopover
                    description={
                        'llm_text_likelihood, burstiness, type_token_ratio, and template_score are '
                        + 'per-doc stylometric signals averaged within each bucket the detector assigned '
                        + '(e.g. "bot", "human", "uncertain"). Higher template_score and lower '
                        + 'type_token_ratio both lean toward templated/automated text.'
                    }
                />
            }
        >
            <div className="desk-table-wrap">
                <table className="table">
                    <thead>
                        <tr>
                            <th>Label</th>
                            <th className="num">Docs</th>
                            <th className="num">LLM likelihood</th>
                            <th className="num">Burstiness</th>
                            <th className="num">Type/token</th>
                            <th className="num">Template score</th>
                        </tr>
                    </thead>
                    <tbody>
                        {buckets.map((b) => (
                            <tr key={b.label}>
                                <td style={{ textTransform: 'capitalize' }}>{b.label}</td>
                                <td className="num">{b.docCount.toLocaleString()}</td>
                                <td className="num">{fmtAvg(b.avgLlmTextLikelihood)}</td>
                                <td className="num">{fmtAvg(b.avgBurstiness)}</td>
                                <td className="num">{fmtAvg(b.avgTypeTokenRatio)}</td>
                                <td className="num">{fmtAvg(b.avgTemplateScore)}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Per-entity bot rates — two-way (officials/collectives vs communities)      //
//  Restored: rows now open BotEntityModal (pre-cutover's per-entity drill-    //
//  down). Degraded: EntityBotRate carries no per-entity sample docs, so the   //
//  modal shows stats only — no flagged-post excerpts for that entity.        //
// --------------------------------------------------------------------------- //

const BOT_SORTERS: ColumnSorter<EntityBotRate>[] = [
    { label: 'bot rate', compare: (a, b) => b.botRatePct - a.botRatePct },
    { label: 'posts scanned', compare: (a, b) => b.totalDocs - a.totalDocs },
    { label: 'name', compare: (a, b) => a.displayName.localeCompare(b.displayName) },
];

function toRanked(item: EntityBotRate, onOpen: (item: EntityBotRate) => void): RankedEntity {
    return {
        entity: { kind: item.kind, displayName: item.displayName },
        rateValue: item.totalDocs > 0 ? formatPct(item.botRatePct) : '—',
        ratePct: item.botRatePct,
        rateColor: botRateColor(item.botRatePct),
        detail: `${item.botDocs.toLocaleString()} of ${item.totalDocs.toLocaleString()} flagged`,
        onClick: () => onOpen(item),
    };
}

function BotEntityModal({ item, onClose }: { item: EntityBotRate; onClose: () => void }) {
    const rateColor = botRateColor(item.botRatePct);
    return (
        <Modal
            isOpen
            onClose={onClose}
            title={item.displayName}
            subtitle="Bot-detection drill-down"
        >
            <EntityHeader entity={{ kind: item.kind, displayName: item.displayName }} />
            <div className="entity-modal-stats">
                <div>
                    <div
                        className="eyebrow"
                        title="Share of this source's scored posts our detector flags as likely automated. A lead, not a verdict."
                    >
                        Suspected bot rate
                    </div>
                    <div className="metric-value" style={{ color: rateColor }}>
                        {item.totalDocs > 0 ? formatPct(item.botRatePct) : '—'}
                    </div>
                </div>
                <div>
                    <div className="eyebrow">Flagged posts</div>
                    <div className="metric-value">{item.botDocs.toLocaleString()}</div>
                </div>
                <div>
                    <div className="eyebrow">Posts scanned</div>
                    <div className="metric-value">{item.totalDocs.toLocaleString()}</div>
                </div>
            </div>
            {/* The pre-redesign modal listed this entity's own flagged-post
                excerpts (BotEntityItem.samples). EntityBotRate carries no
                per-entity sample docs, so that section is dropped rather
                than filled with the whole window's flagged posts under this
                entity's name. */}
            <div className="card-note mt-4">
                Bot flags are probabilistic leads, not verdicts. See "Flagged posts" below
                for excerpts across the whole window.
            </div>
        </Modal>
    );
}

function EntityBotGrid({ byEntity, onOpen }: { byEntity: EntityBotRate[]; onOpen: (item: EntityBotRate) => void }) {
    const officials = byEntity.filter((e) => e.kind === 'official' || e.kind === 'collective');
    const communities = byEntity.filter((e) => e.kind === 'subreddit');
    return (
        <TwoWayGrid>
            <ThreeWayColumn
                header="Politicians, Officials & Collectives"
                byline="Tracked officeholders and party collectives, ranked by the share of their posts our detector flags as likely automated."
                empty="No official posts scored for bot detection in this window."
                items={officials}
                renderItems={(items) => (
                    <RankedEntityList items={items.map((it) => toRanked(it, onOpen))} ariaLabel="Officials by suspected bot rate" />
                )}
                sorters={BOT_SORTERS}
            />
            <ThreeWayColumn
                header="Communities"
                byline="Tracked subreddits, ranked by the share of their posts our detector flags as likely automated."
                empty="No subreddit posts scored for bot detection in this window."
                items={communities}
                renderItems={(items) => (
                    <RankedEntityList items={items.map((it) => toRanked(it, onOpen))} ariaLabel="Communities by suspected bot rate" />
                )}
                sorters={BOT_SORTERS}
            />
        </TwoWayGrid>
    );
}

// --------------------------------------------------------------------------- //
//  Coordination summary — pre-redesign panel restored. The headline metric   //
//  is now the real coordinationIndex (max single-hour share of the           //
//  bot-flagged posting-cadence histogram, restored 2026-07-26): the original //
//  CoordinationStats (accountReuse, identicalTextPairs,                      //
//  avgPostsPerSuspectedAccount) still has no equivalent in /bot-activity.    //
// --------------------------------------------------------------------------- //

function coordinationColor(level: 'low' | 'moderate' | 'high'): string {
    return level === 'high' ? COLORS.negative : level === 'moderate' ? COLORS.warning : 'var(--neutral-700)';
}

function CoordinationSummary({ data }: { data: BotActivityResponse }) {
    const level = coordinationLevel(data.coordinationIndex);
    return (
        <Card
            title="Coordination Indicators"
            subtitle={<DefinitionChip entry="coordination" label="What counts as coordination?" />}
            headerActions={
                <MethodPopover
                    description="Coordination is detected through behavioral analysis including timing patterns, text similarity, and network analysis."
                    limitations={[
                        'Some legitimate coordinated campaigns may be flagged',
                        'Sophisticated actors may evade detection',
                        'Metrics are indicative, not definitive proof',
                    ]}
                />
            }
        >
            <div className="grid-2 gap-3">
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span
                            className="text-sm"
                            title="Max single-hour share of the bot-flagged posting-cadence histogram. 1.0 means every bot-flagged doc in range posted in the same UTC hour."
                        >
                            Coordination index
                        </span>
                        <span className="num font-semibold" style={{ color: coordinationColor(level) }}>
                            {level} ({data.coordinationIndex.toFixed(2)})
                        </span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm" title="Share of scored posts our detector flags as likely automated.">Suspected automation rate</span>
                        <span className="num font-semibold">{formatPct(data.automationRatePct, { decimals: 0 })}</span>
                    </div>
                </div>
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Bot-scored posts</span>
                        <span className="num font-semibold">{data.botScoredDocCount.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Analyzed docs</span>
                        <span className="num font-semibold">{data.analyzedDocCount.toLocaleString()}</span>
                    </div>
                </div>
            </div>
            <div className="card-note mt-4">
                These metrics indicate potential automation but are not definitive proof of
                coordinated or malicious activity. Account-reuse and identical-text-pair
                coordination scoring is not part of this build's data yet.
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Narrative amplification — 2-up tiles, degraded to the botFractionPct       //
//  the response carries (no whyFlagged/hashtags/targets breakdown today).     //
// --------------------------------------------------------------------------- //

function amplificationConfidence(botFractionPct: number): 'high' | 'medium' | 'low' {
    if (botFractionPct >= 50) return 'high';
    if (botFractionPct >= 20) return 'medium';
    return 'low';
}

function ConfidenceBadge({ level }: { level: 'high' | 'medium' | 'low' }) {
    if (level === 'high') return <span className="badge badge-negative" title="High share of bot-authored member docs">High likelihood</span>;
    if (level === 'medium') return <span className="badge badge-warning" title="Medium share of bot-authored member docs">Medium likelihood</span>;
    return <span className="badge badge-neutral" title="Low share of bot-authored member docs">Low likelihood</span>;
}

function NarrativeAmplificationCard({ narrative }: { narrative: BotPushedNarrative }) {
    const [modalOpen, setModalOpen] = useState(false);
    const level = amplificationConfidence(narrative.botFractionPct);

    return (
        <>
            <Card>
                <div className="flex items-start justify-between mb-3" style={{ gap: 'var(--space-3)', flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                        <h4 className="font-semibold">{narrative.name || '(unnamed narrative)'}</h4>
                        <div className="flex items-center gap-2 mt-1" style={{ flexWrap: 'wrap' }}>
                            <ConfidenceBadge level={level} />
                            <span className="eyebrow num">
                                {formatPct(narrative.botFractionPct, { decimals: 0 })} bot-authored
                            </span>
                            <a
                                href={deepLinkHref('narratives', { open: String(narrative.narrativeId) })}
                                className="example-row-link"
                                title="Open this story on the Political Narratives page"
                            >
                                See the full story
                            </a>
                        </div>
                    </div>
                    <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setModalOpen(true)}
                        aria-haspopup="dialog"
                    >
                        View details
                    </button>
                </div>
                <p className="text-xs text-muted" style={{ margin: 0 }}>
                    {narrative.botAuthoredDocCount.toLocaleString()} of {narrative.memberDocCount.toLocaleString()} member docs
                    authored by a bot-scored account.
                </p>
            </Card>

            <Modal
                isOpen={modalOpen}
                onClose={() => setModalOpen(false)}
                kicker="Narrative amplification"
                title={narrative.name || '(unnamed narrative)'}
                subtitle={`${narrative.botAuthoredDocCount.toLocaleString()} of ${narrative.memberDocCount.toLocaleString()} member docs bot-authored · ${level} likelihood`}
            >
                {/* The pre-redesign modal listed why-flagged signals plus
                    derived hashtag/target chips (NarrativeAmplification.
                    whyFlagged/topHashtags/targets). BotPushedNarrative
                    carries none of those — this section is dropped rather
                    than invented. */}
                <h3 className="card-title mb-2">Flagged posts</h3>
                <SampleCardList
                    samples={narrative.samples}
                    sampleNote="Bot-authored member docs, confidence-sorted — a sample, not every flag."
                    emptyNote="No sample docs stored for this narrative."
                />
            </Modal>
        </>
    );
}

// --------------------------------------------------------------------------- //
//  Flagged accounts                                                           //
// --------------------------------------------------------------------------- //

function FlaggedAccountModal({ account, onClose }: { account: FlaggedAccount; onClose: () => void }) {
    return (
        <Modal
            isOpen
            onClose={onClose}
            title={account.displayName || account.handle || `Author #${account.authorId}`}
            subtitle="Bot-detection drill-down"
        >
            <EntityHeader entity={{ kind: null, displayName: account.displayName || account.handle || 'Unknown', lean: account.lean }} />
            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged post share</div>
                    <div className="metric-value">{(account.flaggedPostShare * 100).toFixed(0)}%</div>
                </div>
                <div>
                    <div className="eyebrow">Sample count</div>
                    <div className="metric-value">{account.sampleCount.toLocaleString()}</div>
                </div>
                {account.followersCount != null && (
                    <div>
                        <div className="eyebrow">Followers</div>
                        <div className="metric-value">{account.followersCount.toLocaleString()}</div>
                    </div>
                )}
            </div>
            <h3 className="card-title mt-4 mb-2">Flagged posts from this account</h3>
            <SampleCardList
                samples={account.samples}
                sampleNote="The highest-confidence flagged posts from this account — a sample, not every flag."
                emptyNote="No sample posts stored for this account in this window."
            />
        </Modal>
    );
}

function FlaggedAccountsCard({ accounts }: { accounts: FlaggedAccount[] }) {
    const [active, setActive] = useState<FlaggedAccount | null>(null);
    if (accounts.length === 0) return null;
    return (
        <Card
            title="Flagged accounts"
            subtitle="Example bot-scored accounts with enough footprint (posts + followers) to profile individually."
        >
            <div className="flex flex-col gap-2">
                {accounts.map((a) => (
                    <button
                        key={a.authorId}
                        type="button"
                        className="ranked-entity-row"
                        onClick={() => setActive(a)}
                    >
                        <span className="ranked-entity-main">
                            <span className="ranked-entity-name">
                                {a.displayName || a.handle || `Author #${a.authorId}`}
                            </span>
                            <span className="ranked-entity-desc">{a.platform} · {a.sampleCount.toLocaleString()} posts</span>
                        </span>
                        <span className="ranked-entity-rate">
                            <span className="ranked-entity-rate-value" style={{ color: botRateColor(a.flaggedPostShare * 100) }}>
                                {(a.flaggedPostShare * 100).toFixed(0)}%
                            </span>
                        </span>
                    </button>
                ))}
            </div>
            {active && <FlaggedAccountModal account={active} onClose={() => setActive(null)} />}
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Page                                                                       //
// --------------------------------------------------------------------------- //

interface BotActivityProfilerProps {
    filters: Filters;
}

function BotActivityProfiler({ filters }: BotActivityProfilerProps) {
    const [activeEntity, setActiveEntity] = useState<EntityBotRate | null>(null);
    const { data, loading, error, refetch } = useFetch<BotActivityResponse>(
        () => fetchBotActivity(filters.timeRange),
        [filters.timeRange],
        `bot-activity:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(), [], 'snapshot-status',
    );

    if (error) return <ErrorState message={error.message} onRetry={refetch} />;
    if (loading) {
        return (
            <div className="flex flex-col gap-6">
                <div className="grid-3"><LoadingCard /><LoadingCard /><LoadingCard /></div>
                <LoadingCard />
            </div>
        );
    }
    if (!data) return <EmptyState title="No bot-activity data available" />;

    const refreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={buildTickerItems(data)}
                    refreshed={refreshed}
                    ariaLabel="Bot detector overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Suspected automation = the share of scored posts our detector flags as '
                                + 'likely automated. These are probabilistic leads, not verdicts.'
                            }
                        />
                    }
                />
                <RangeCaption range={data.range} />
            </div>

            <div className="col-span-12">
                <div className="reads-as-today">
                    <p className="lead" style={{ margin: 0 }}>{readsAsToday(data)}</p>
                </div>
            </div>

            {/* The chain of evidence — the page's signature strip. */}
            <div className="col-span-12">
                <CoordinationEvidencePanel postingCadence={data.postingCadence} />
            </div>

            <div className="col-span-12">
                <EntityBotGrid byEntity={data.byEntity} onOpen={setActiveEntity} />
                <p className="card-note">
                    News articles are not bot-scored: articles are not accounts, so an outlet has no
                    automation rate. Bot detection covers social posts only.
                </p>
            </div>

            {activeEntity && (
                <BotEntityModal item={activeEntity} onClose={() => setActiveEntity(null)} />
            )}

            <div className="col-span-12">
                <CoordinationSummary data={data} />
            </div>

            <div className="col-span-12 bot-section-label">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                    Narratives with Suspected Bot Amplification
                </div>
                <div className="text-xs text-muted" style={{ lineHeight: 'var(--leading-relaxed)' }}>
                    Each row below is a narrative whose amplifying accounts score as likely bots. Open
                    View details to see the bot-authored posts carrying it.
                </div>
            </div>

            {data.botPushedNarratives.map((narrative) => (
                <div key={narrative.narrativeId} className="col-span-6">
                    <NarrativeAmplificationCard narrative={narrative} />
                </div>
            ))}
            {data.botPushedNarratives.length === 0 && (
                <div className="col-span-12">
                    <Card>
                        <p className="text-sm text-muted" style={{ margin: 0 }}>
                            No flagged post in this window belongs to a tracked narrative yet.
                            This section fills in when suspected-bot posts overlap with the
                            recurring claims on the Narratives page.
                        </p>
                    </Card>
                </div>
            )}

            <div className="col-span-6"><AccountAgeCard buckets={data.accountAgeBuckets} /></div>
            <div className="col-span-6"><BehavioralSignalsCard buckets={data.behavioralSignals} /></div>

            <div className="col-span-12"><FlaggedAccountsCard accounts={data.flaggedAccounts} /></div>

            <div className="col-span-12">
                <Card title="Flagged posts">
                    <SampleCardList
                        samples={data.flaggedDocs}
                        sampleNote="The highest-confidence flagged posts across the whole window — a sample, not every flag."
                        emptyNote="No posts were flagged in this window."
                    />
                </Card>
            </div>

            <div className="col-span-12">
                <CollapsibleInfo>
                    <p className="text-sm">
                        This page flags accounts and posts in our political-content sample that look
                        automated. The detector scores each account from behavioral signals — posting
                        rate, text repetition, account age.
                    </p>
                    <p className="text-sm">
                        Some real users post in bot-like ways, and some real bots post like humans.
                        Treat flags as <strong>leads, not verdicts</strong>.
                    </p>
                </CollapsibleInfo>
            </div>
        </div>
    );
}

export default BotActivityProfiler;
