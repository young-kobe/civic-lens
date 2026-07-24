import { useState } from 'react';
import {
    Card, CollapsibleInfo, EmptyState, EntityHeader, ErrorState,
    GlobalTicker, LoadingCard, MethodPopover, Modal, RangeCaption,
    RankedEntityList, SampleCardList, ThreeWayColumn, TwoWayGrid,
} from '../components/common';
import type { ColumnSorter, RankedEntity, TickerItem } from '../components/common';
import { fetchBotActivity, fetchSnapshotStatus } from '../services/api';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { COLORS } from '../theme';
import type {
    AccountAgeBucket, BehavioralSignalBucket, BotActivityResponse, EntityBotRate,
    Filters, FlaggedAccount, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  Phase 10 adaptation note: the pre-redesign coordination index, top         //
//  amplified domain clusters, posting-cadence heatmap, copy-paste-similarity  //
//  distribution, and per-narrative "why flagged" LLM breakdown have no        //
//  equivalent in the strictly-live /bot-activity response (see               //
//  analysis/src/api/models/bots.py) and are removed rather than faked. What  //
//  the new response DOES carry — per-label behavioral-signal averages,       //
//  account-age buckets, per-entity bot rates, bot-pushed narratives with     //
//  evidence samples, and flagged-account/doc evidence — drives this page.    //
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
            <div className="flex flex-col gap-2">
                {buckets.filter((b) => b.accountCount > 0).map((b) => {
                    const pct = (b.accountCount / total) * 100;
                    const isNewest = b.ageRange.startsWith('<');
                    return (
                        <div key={b.ageRange}>
                            <div className="flex justify-between text-sm mb-1">
                                <span>{b.ageRange}</span>
                                <span className="num text-muted">
                                    {b.accountCount.toLocaleString()} ({pct.toFixed(0)}%)
                                </span>
                            </div>
                            <div style={{ height: 6, background: 'var(--neutral-100)', overflow: 'hidden' }}>
                                <div
                                    style={{
                                        width: `${pct}%`, height: '100%',
                                        background: isNewest ? COLORS.warning : COLORS.chartAccent,
                                    }}
                                />
                            </div>
                        </div>
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
// --------------------------------------------------------------------------- //

const BOT_SORTERS: ColumnSorter<EntityBotRate>[] = [
    { label: 'bot rate', compare: (a, b) => b.botRatePct - a.botRatePct },
    { label: 'posts scanned', compare: (a, b) => b.totalDocs - a.totalDocs },
    { label: 'name', compare: (a, b) => a.displayName.localeCompare(b.displayName) },
];

function toRanked(item: EntityBotRate): RankedEntity {
    return {
        entity: { kind: item.kind, displayName: item.displayName },
        rateValue: item.totalDocs > 0 ? formatPct(item.botRatePct) : '—',
        ratePct: item.botRatePct,
        rateColor: botRateColor(item.botRatePct),
        detail: `${item.botDocs.toLocaleString()} of ${item.totalDocs.toLocaleString()} flagged`,
    };
}

function EntityBotGrid({ byEntity }: { byEntity: EntityBotRate[] }) {
    const officials = byEntity.filter((e) => e.kind === 'official' || e.kind === 'collective');
    const communities = byEntity.filter((e) => e.kind === 'subreddit');
    return (
        <TwoWayGrid>
            <ThreeWayColumn
                header="Politicians, Officials & Collectives"
                byline="Tracked officeholders and party collectives, ranked by the share of their posts our detector flags as likely automated."
                empty="No official posts scored for bot detection in this window."
                items={officials}
                renderItems={(items) => <RankedEntityList items={items.map(toRanked)} ariaLabel="Officials by suspected bot rate" />}
                sorters={BOT_SORTERS}
            />
            <ThreeWayColumn
                header="Communities"
                byline="Tracked subreddits, ranked by the share of their posts our detector flags as likely automated."
                empty="No subreddit posts scored for bot detection in this window."
                items={communities}
                renderItems={(items) => <RankedEntityList items={items.map(toRanked)} ariaLabel="Communities by suspected bot rate" />}
                sorters={BOT_SORTERS}
            />
        </TwoWayGrid>
    );
}

// --------------------------------------------------------------------------- //
//  Bot-pushed narratives                                                      //
// --------------------------------------------------------------------------- //

function BotPushedNarrativesCard({ data }: { data: BotActivityResponse }) {
    if (data.botPushedNarratives.length === 0) return null;
    return (
        <Card
            title="Narratives with suspected bot amplification"
            subtitle="Recurring claims ranked by the share of their in-range member docs authored by a bot-scored account."
        >
            <div className="flex flex-col gap-4">
                {data.botPushedNarratives.map((n) => (
                    <div key={n.narrativeId} className="bot-narrative-row">
                        <div className="flex items-baseline justify-between" style={{ gap: 'var(--space-3)' }}>
                            <span className="font-semibold">{n.name || '(unnamed narrative)'}</span>
                            <span className="eyebrow num">
                                {formatPct(n.botFractionPct, { decimals: 0 })} bot-authored
                            </span>
                        </div>
                        <p className="text-xs text-muted" style={{ margin: '2px 0 var(--space-2)' }}>
                            {n.botAuthoredDocCount.toLocaleString()} of {n.memberDocCount.toLocaleString()} member docs
                        </p>
                        <SampleCardList
                            samples={n.samples}
                            sampleNote="Bot-authored member docs, confidence-sorted — a sample, not every flag."
                            emptyNote="No sample docs stored for this narrative."
                        />
                    </div>
                ))}
            </div>
        </Card>
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
                    <div className="eyebrow">Bot score</div>
                    <div className="metric-value">{(account.botScore * 100).toFixed(0)}%</div>
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
                            <span className="ranked-entity-rate-value" style={{ color: botRateColor(a.botScore * 100) }}>
                                {(a.botScore * 100).toFixed(0)}%
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

            <div className="col-span-6"><AccountAgeCard buckets={data.accountAgeBuckets} /></div>
            <div className="col-span-6"><BehavioralSignalsCard buckets={data.behavioralSignals} /></div>

            <div className="col-span-12">
                <EntityBotGrid byEntity={data.byEntity} />
                <p className="card-note">
                    News articles are not bot-scored: articles are not accounts, so an outlet has no
                    automation rate. Bot detection covers social posts only.
                </p>
            </div>

            <div className="col-span-12"><BotPushedNarrativesCard data={data} /></div>
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
