import { useEffect, useState } from 'react';
import {
    Card, CollapsibleInfo, DefinitionChip, EmptyState, EntityHeader,
    EntityHubLinks, ErrorState,
    GlobalTicker,
    LoadingCard, MethodPopover, Modal, PostCardList,
    RangeCaption, RankedEntityList, ThreeWayColumn, TwoWayGrid,
    entityExternalUrl, flaggedExampleToPostCard, parseEntityParam,
    LeanLabel as LeanLabelChip,
} from '../components/common';
import type { PostCardData } from '../components/common/PostCard';
import { deepLinkHref, useDeepLinkParam } from '../services/deepLink';
import { coordinationLevel } from '../services/glossary';
import { CoordinationEvidencePanel } from './bots/CoordinationEvidencePanel';
import { PostingCadenceHeatmap } from './bots/PostingCadenceHeatmap';
import type { ColumnSorter, RankedEntity, TickerItem } from '../components/common';
import { fetchBotActivity, fetchSnapshotStatus } from '../services/api';
import { useFetch } from '../services/useFetch';
import { formatRefreshedAgo, pipelineRunTimestamp } from '../services/freshness';
import { formatPct } from '../services/format';
import { COLORS } from '../theme';
import type {
    AccountAgeBucket, BehavioralSignalBucket, BotActivityResponse, BotEntityItem,
    BotPushedNarrative, CoordinationStats, Filters, FlaggedAccount, PostingCadenceCell,
    SampleDoc, SnapshotStatusResponse,
} from '../types';

// --------------------------------------------------------------------------- //
//  sampleDocToPostCard — local adapter for the plain SampleDoc[] shape        //
//  (BotPushedNarrative.samples, FlaggedAccount.samples, data.flaggedDocs).    //
//  No shared adapter covers SampleDoc today (PostCard.tsx's adapters cover    //
//  ClassificationSample/FlaggedExample/PropagandaExample); SampleDoc is a     //
//  thin doc reference (docId, sourceUrl, snippet, confidence, publishedAt) —  //
//  everything PostCardData needs beyond that (title, author, engagement) is   //
//  simply absent here, not fabricated.                                       //
// --------------------------------------------------------------------------- //

function domainFromUrl(url: string): string | null {
    try {
        return new URL(url).hostname.replace(/^www\./, '');
    } catch {
        return null;
    }
}

function unixSecondsFromIso(iso: string | null | undefined): number | null {
    if (!iso) return null;
    const t = Date.parse(iso);
    return Number.isNaN(t) ? null : Math.floor(t / 1000);
}

function sampleDocToPostCard(s: SampleDoc): PostCardData {
    const domain = domainFromUrl(s.sourceUrl);
    return {
        doc_id: s.docId,
        source_type: 'unknown',
        source_label: domain ? `Source · ${domain}` : 'Source unknown',
        title: null,
        text: s.snippet ?? null,
        published_at: unixSecondsFromIso(s.publishedAt),
        url: s.sourceUrl,
        confidence: s.confidence,
        domain,
    };
}

// --------------------------------------------------------------------------- //
//  Restoration note (docs/audit-trail/ui/2026-07-27-bot-activity-restoration  //
//  records the full rationale). This page restores the pre-cutover           //
//  BotActivityProfiler's geometry — the two-way officials/public grid with   //
//  per-entity drill-down, coordination summary, per-narrative amplification   //
//  cards, and the behavioral-signals band — wired to the strictly-live       //
//  /bot-activity response (analysis/src/api/models/bots.py). Two pre-cutover //
//  data sources have no Postgres equivalent yet and are rendered as honest   //
//  "not measured" states rather than invented: identicalTextPairs            //
//  (CoordinationStats — the O(n^2) shingle scan was never ported) and        //
//  pairwise copy-paste-similarity buckets (no engine output at all; the      //
//  closest available proxy, per-label mean template_score, fills that slot   //
//  with an explicit caveat). The old day-by-hour posting-cadence heatmap IS  //
//  restored: postingCadenceGrid now carries real (dayOfWeek, hour) UTC       //
//  buckets, unlike the server-local-hour data that got it dropped originally.//
// --------------------------------------------------------------------------- //

function botRateColor(ratePct: number): string {
    return ratePct > 10
        ? COLORS.negative
        : ratePct > 3
            ? COLORS.warning
            : 'var(--neutral-700)';
}

/**
 * True for narrative-name artifacts that leaked a raw signal field (e.g.
 * `account_age=None days`, trailing `=`). Restored defensively from the
 * pre-cutover filter (`analysis/src/engine/bot.py::_sanitize_llm_indicators`
 * sanitizes at write time now) — narrative.name is an editorial registry
 * name today, not an LLM label, so this should be a no-op in practice.
 */
function isNoiseLabel(label: string | null | undefined): boolean {
    if (!label) return true;
    const text = label.trim();
    if (!text) return true;
    if (/=\s*(None|null|undefined|0)\b/i.test(text)) return true;
    if (/=\s*$/.test(text)) return true;
    return false;
}

/** Headline derived from the automation rate + the top bot-pushed narrative.
 *  Pre-cutover also named the top amplified link-domain cluster
 *  (`overview.topClusters`) — that field has no equivalent in the current
 *  response (analysis/src/api/models/bots.py carries no domain-cluster
 *  rollup), so this reads as automation rate + narrative only rather than
 *  guessing a domain. */
function readsAsToday(data: BotActivityResponse): string {
    const rate = data.automationRatePct;
    const ratePct = formatPct(rate, { decimals: 0 });
    const parts: string[] = [];
    if (rate > 10) {
        parts.push(`A high share of the posts we scored look automated — roughly ${ratePct}.`);
    } else if (rate > 3) {
        parts.push(`Some of the posts we scored look automated — about ${ratePct}.`);
    } else {
        parts.push(`Most posts we scored look like real people — only about ${ratePct} look automated.`);
    }
    const topNarrative = data.botPushedNarratives.find((n) => !isNoiseLabel(n.name));
    if (topNarrative) {
        const count = topNarrative.botAuthoredDocCount.toLocaleString();
        parts.push(`${count} bot-authored posts are amplifying the same narrative: "${topNarrative.name}".`);
    }
    return parts.join(' ');
}

function buildBotTickerItems(data: BotActivityResponse): TickerItem[] {
    const rate = data.automationRatePct;
    const rateTone = rate > 10 ? 'warning' : rate > 3 ? 'neutral' : 'positive';

    // Pre-cutover's fourth slot was overview.confidence (a detector-wide
    // ConfidenceLevel) — that field does not exist on BotActivityResponse.
    // Analyzed docs is the closest honestly-available volume figure, so it
    // fills the slot instead of dropping the ticker to three items.
    return [
        {
            label: 'Suspected automation',
            value: formatPct(rate, { decimals: 0 }),
            hint: 'of posts',
            tone: rateTone as TickerItem['tone'],
            emphasis: true,
            ariaLabel: `Suspected automation rate ${rate.toFixed(1)} percent`,
        },
        {
            label: 'Coordination',
            value: coordinationLevel(data.coordinationIndex),
            hint: `${data.coordinationIndex.toFixed(2)} / 1`,
        },
        {
            label: 'Flagged Posts',
            value: data.totalFlaggedPosts.toLocaleString(),
        },
        {
            label: 'Posts analyzed',
            value: data.analyzedDocCount.toLocaleString(),
        },
    ];
}

// --------------------------------------------------------------------------- //
//  Amplification by tier — two-way entity grid                                //
// --------------------------------------------------------------------------- //

function BotEntityModal({ item, onClose }: { item: BotEntityItem; onClose: () => void }) {
    const profile = item.entityProfile;
    const rateColor = botRateColor(item.botRatePct);
    const sourceUrl = entityExternalUrl(profile);

    return (
        <Modal
            isOpen
            onClose={onClose}
            title={profile.displayName}
            subtitle="Bot-detection drill-down"
        >
            <EntityHeader profile={profile} />
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
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{item.totalDocs.toLocaleString()}</div>
                </div>
            </div>

            <h3 className="card-title mt-4 mb-2">Flagged posts from this source</h3>
            <PostCardList
                posts={item.samples.map(flaggedExampleToPostCard)}
                sampleNote="The highest-confidence flagged posts from this source — a sample, not every flag."
                emptyNote={item.botDocs > 0
                    ? 'Flagged posts exist but their excerpts are not in this snapshot yet — they will appear on the next data refresh.'
                    : 'No posts from this source were flagged in this window.'}
            />
            <div className="card-note mt-4">
                Bot flags are probabilistic leads, not verdicts. Every excerpt links
                back to the original post so you can judge it yourself.
            </div>
            {sourceUrl && (
                <div className="mt-3">
                    <a href={sourceUrl} target="_blank" rel="noreferrer" className="example-row-link">
                        Visit {profile.displayName}
                    </a>
                </div>
            )}
            <EntityHubLinks entityId={profile.entityId ?? null} currentTab="bots" />
        </Modal>
    );
}

const BOT_SORTERS: ColumnSorter<BotEntityItem>[] = [
    { label: 'bot rate', compare: (a, b) => b.botRatePct - a.botRatePct },
    { label: 'posts scored', compare: (a, b) => b.totalDocs - a.totalDocs },
    { label: 'name', compare: (a, b) => a.entityProfile.displayName.localeCompare(b.entityProfile.displayName) },
];

function BotThreeWayGrid({
    data, onOpen,
}: {
    data: BotActivityResponse;
    onOpen: (item: BotEntityItem) => void;
}) {
    // Two tiers only: news is not bot-scored (articles are not accounts) —
    // the response carries no byNewsOutlet field at all.
    const ranked = (label: string) => (items: BotEntityItem[]) => (
        <RankedEntityList
            items={items.map((it): RankedEntity => ({
                profile: it.entityProfile,
                rateValue: it.totalDocs > 0 ? formatPct(it.botRatePct) : '—',
                ratePct: it.botRatePct,
                rateColor: botRateColor(it.botRatePct),
                detail: `${it.botDocs.toLocaleString()} of ${it.totalDocs.toLocaleString()} flagged`,
                onClick: () => onOpen(it),
            }))}
            ariaLabel={label}
        />
    );

    return (
        <TwoWayGrid>
            <ThreeWayColumn
                header="Politicians & Officials"
                byline="Tracked officeholders, ranked by the share of their posts our detector flags as likely automated."
                empty="No official posts scored for bot detection."
                items={data.byOfficial}
                renderItems={ranked('Officials by suspected bot rate')}
                sorters={BOT_SORTERS}
            />
            <ThreeWayColumn
                header="The Public"
                byline="Political subreddits, plus X users we don't track individually, ranked by the share of their posts our detector flags as likely automated."
                empty="No public social posts scored for bot detection."
                items={data.byGeneralPublic}
                renderItems={ranked('Public sources by suspected bot rate')}
                sorters={BOT_SORTERS}
            />
        </TwoWayGrid>
    );
}

// --------------------------------------------------------------------------- //
//  Narrative amplification — 2-up tiles. samples ARE SampleDoc[] here, so     //
//  PostCardList renders them directly (no adaptation needed, unlike           //
//  BotEntityItem.samples above). Pre-cutover's whyFlagged/topHashtags/        //
//  targets breakdown has no equivalent in BotPushedNarrative — dropped        //
//  rather than invented.                                                     //
// --------------------------------------------------------------------------- //

function amplificationConfidence(botFractionPct: number): 'high' | 'medium' | 'low' {
    if (botFractionPct >= 50) return 'high';
    if (botFractionPct >= 20) return 'medium';
    return 'low';
}

function ConfidenceBadge({ level }: { level: 'high' | 'medium' | 'low' }) {
    if (level === 'high') return <span className="badge badge-negative" title="High share of this story's posts authored by a bot-scored account">High likelihood</span>;
    if (level === 'medium') return <span className="badge badge-warning" title="Medium share of this story's posts authored by a bot-scored account">Medium likelihood</span>;
    return <span className="badge badge-neutral" title="Low share of this story's posts authored by a bot-scored account">Low likelihood</span>;
}

function NarrativeAmplificationCard({ narrative }: { narrative: BotPushedNarrative }) {
    const [modalOpen, setModalOpen] = useState(false);
    if (isNoiseLabel(narrative.name)) return null;
    const level = amplificationConfidence(narrative.botFractionPct);

    return (
        <>
            <Card>
                <div className="flex items-start justify-between mb-3" style={{ gap: 'var(--space-3)', flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                        <h4 className="font-semibold">{narrative.name}</h4>
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
                    {narrative.botAuthoredDocCount.toLocaleString()} of {narrative.memberDocCount.toLocaleString()} posts in this
                    story authored by a bot-scored account.
                </p>
            </Card>

            <Modal
                isOpen={modalOpen}
                onClose={() => setModalOpen(false)}
                kicker="Narrative amplification"
                title={narrative.name}
                subtitle={`${narrative.botAuthoredDocCount.toLocaleString()} of ${narrative.memberDocCount.toLocaleString()} posts in this story bot-authored · ${level} likelihood`}
            >
                <h3 className="card-title mb-2">Flagged posts</h3>
                <PostCardList
                    posts={narrative.samples.map(sampleDocToPostCard)}
                    sampleNote="Bot-authored posts in this story, confidence-sorted — a sample, not every flag."
                    emptyNote="No sample docs stored for this narrative."
                />
            </Modal>
        </>
    );
}

// --------------------------------------------------------------------------- //
//  Coordination summary                                                       //
// --------------------------------------------------------------------------- //

function CoordinationSummary({ data }: { data: CoordinationStats }) {
    return (
        <Card
            title="Coordination Indicators"
            subtitle={<DefinitionChip entry="coordination" label="What counts as coordination?" />}
            headerActions={
                <MethodPopover
                    description="Coordination is detected from behavioral signals: suspected accounts posting at the same times, using near-identical wording, or sharing the same link targets."
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
                        <span className="text-sm" title="Share of suspected-bot accounts that posted more than once in this sample.">Suspected accounts posting more than once</span>
                        <span className="num font-semibold">{formatPct(data.accountReuse * 100, { decimals: 0 })}</span>
                    </div>
                </div>
                <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center" style={{ padding: '6px 0', borderBottom: '1px solid var(--neutral-150)' }}>
                        <span className="text-sm">Identical text pairs detected</span>
                        <span className="num font-semibold">
                            {data.identicalTextPairs != null ? data.identicalTextPairs.toLocaleString() : 'not measured'}
                        </span>
                    </div>
                    <div className="flex justify-between items-center" style={{ padding: '6px 0' }}>
                        <span className="text-sm">Avg posts per suspected account</span>
                        <span className="num font-semibold">{data.avgPostsPerSuspectedAccount.toFixed(1)}</span>
                    </div>
                </div>
            </div>
            <div className="card-note mt-4">
                These metrics indicate potential coordination but are not definitive proof of automated
                or malicious activity. Identical-text-pair detection (the pre-cutover O(n^2) shingle scan)
                has not been ported to this build — the count above reads "not measured", never a guess,
                until it is.
            </div>
        </Card>
    );
}

// --------------------------------------------------------------------------- //
//  Behavioral signals — four cards as direct children of the outer dashboard   //
//  grid, restoring the pre-cutover 3-across col-span-4 layout plus the        //
//  restored day-by-hour heatmap. Account age keeps its pre-cutover slot, now  //
//  fed by the top-level accountAgeBuckets field (percentage already on the    //
//  wire). Text-similarity has no data source; the per-label mean             //
//  template_score table is the closest honest proxy and fills that slot with //
//  an explicit caveat rather than a bare empty card. Link-domain             //
//  concentration has no data source at all — honest "not measured" card.     //
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

function AccountAgeCard({ buckets }: { buckets: AccountAgeBucket[] }) {
    const total = buckets.reduce((s, b) => s + b.accountCount, 0);
    return (
        <Card title="Account Age Distribution">
            {total === 0 ? (
                <p className="text-sm text-muted" style={{ margin: 0 }}>
                    No bot-flagged accounts with a known creation date in this window.
                </p>
            ) : (
                <>
                    <div className="flex flex-col gap-2">
                        {buckets.filter((b) => b.accountCount > 0).map((b) => {
                            const isNewest = b.ageRange.startsWith('<');
                            return (
                                <div
                                    key={b.ageRange}
                                    title={isNewest
                                        ? `Newest-account bucket (${b.ageRange}) — highlighted because freshly created accounts skew toward automation. ${formatPct(b.percentage, { decimals: 0 })} of bot-flagged accounts.`
                                        : `Established-account bucket (${b.ageRange}) — ${formatPct(b.percentage, { decimals: 0 })} of bot-flagged accounts.`}
                                >
                                    <div className="flex justify-between text-sm mb-1">
                                        <span>{b.ageRange}</span>
                                        <span className="num text-muted">{formatPct(b.percentage, { decimals: 0 })}</span>
                                    </div>
                                    <div style={{ height: '6px', background: 'var(--neutral-100)', overflow: 'hidden' }}>
                                        <div
                                            style={{
                                                width: `${b.percentage}%`,
                                                height: '100%',
                                                background: isNewest ? COLORS.warning : COLORS.chartAccent,
                                            }}
                                        />
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                    <div className="chart-swatch-legend" aria-hidden>
                        <span className="chart-swatch-item">
                            <span className="chart-swatch" style={{ background: COLORS.warning }} />
                            Newest accounts
                        </span>
                        <span className="chart-swatch-item">
                            <span className="chart-swatch" style={{ background: COLORS.chartAccent }} />
                            Older accounts
                        </span>
                    </div>
                    {(() => {
                        const youngest = buckets.find((b) => b.ageRange.startsWith('<'));
                        if (!youngest) return null;
                        return (
                            <div className="card-note mt-4">
                                {formatPct(youngest.percentage, { decimals: 0 })} of bot-flagged accounts are
                                in the {youngest.ageRange} bucket.
                            </div>
                        );
                    })()}
                </>
            )}
        </Card>
    );
}

function fmtAvg(v: number | null): string {
    return v == null ? '—' : v.toFixed(2);
}

function TextSimilarityProxyCard({ buckets }: { buckets: BehavioralSignalBucket[] }) {
    return (
        <Card
            title="Text Similarity Distribution"
            subtitle={<DefinitionChip entry="template_proxy" label="Templated-language score" />}
            headerActions={
                <MethodPopover
                    description={
                        'Pairwise copy-paste-similarity buckets (this build\'s pre-cutover measure) are not '
                        + 'computed yet. The templated-language score — mean per-label fill-in-the-blank '
                        + 'score — is the closest signal this build measures toward the same question: do '
                        + 'this label\'s posts read as boilerplate/repeated text?'
                    }
                />
            }
        >
            <div className="text-xs text-muted mb-3">
                Pairwise copy-paste similarity across suspected-bot posts is not measured in this build.
                Shown instead: mean templated-language score per detector label — higher values lean
                toward templated/repeated text.
            </div>
            {buckets.length === 0 ? (
                <p className="text-sm text-muted" style={{ margin: 0 }}>No labeled posts in this window.</p>
            ) : (
                <div className="flex flex-col gap-3">
                    {buckets.map((b) => (
                        <SimilarityBar
                            key={b.label}
                            label={`${b.label} (${b.docCount.toLocaleString()} posts)`}
                            value={b.avgTemplateScore != null ? b.avgTemplateScore * 100 : 0}
                            color={b.label === 'bot' ? COLORS.negative : b.label === 'suspicious' ? COLORS.warning : COLORS.chartAccent}
                            title={`Mean templated-language score for "${b.label}": ${fmtAvg(b.avgTemplateScore)} (a proxy, not a copy-paste-similarity measure)`}
                        />
                    ))}
                </div>
            )}
        </Card>
    );
}

function LinkDomainConcentrationCard() {
    return (
        <Card
            title="Link Domain Concentration"
            subtitle="How often suspected-bot posts link to the same handful of sites."
        >
            <p className="text-sm text-muted" style={{ margin: 0 }}>
                Shared-link-domain concentration across suspected-bot posts is not part of this build's
                bot-activity data yet. High concentration of links to a small number of domains would
                indicate coordinated promotion once this measure lands.
            </p>
        </Card>
    );
}

interface BehavioralSignalsPanelProps {
    accountAgeBuckets: AccountAgeBucket[];
    behavioralSignals: BehavioralSignalBucket[];
    postingCadenceGrid: PostingCadenceCell[];
}

function BehavioralSignalsPanel({ accountAgeBuckets, behavioralSignals, postingCadenceGrid }: BehavioralSignalsPanelProps) {
    return (
        <>
            <div className="col-span-4"><AccountAgeCard buckets={accountAgeBuckets} /></div>
            <div className="col-span-4"><TextSimilarityProxyCard buckets={behavioralSignals} /></div>
            <div className="col-span-4"><LinkDomainConcentrationCard /></div>
            <div className="col-span-12">
                <Card
                    title="Posting Cadence by Day and Hour"
                    subtitle="Bot-flagged posts, bucketed by day of week and hour of day (UTC)."
                >
                    <PostingCadenceHeatmap cells={postingCadenceGrid} />
                </Card>
            </div>
        </>
    );
}

// --------------------------------------------------------------------------- //
//  Flagged accounts — current-era addition, kept and slotted into the         //
//  restored geometry. FlaggedAccount carries no EntityProfile (no key/blurb), //
//  so the modal renders account identity directly rather than routing         //
//  through EntityHeader/EntityProfileCard.                                   //
// --------------------------------------------------------------------------- //

function FlaggedAccountModal({ account, onClose }: { account: FlaggedAccount; onClose: () => void }) {
    return (
        <Modal
            isOpen
            onClose={onClose}
            title={account.displayName || account.handle || `Author #${account.authorId}`}
            subtitle="Bot-detection drill-down"
        >
            <div className="entity-modal-head">
                <div>
                    <p className="lead" style={{ margin: 0 }}>
                        {account.platform} account
                        {account.handle ? ` · @${account.handle}` : ''}
                    </p>
                    {account.lean && <LeanLabelChip lean={account.lean} variant="chip" />}
                </div>
            </div>
            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged post share</div>
                    <div className="metric-value">{formatPct(account.flaggedPostShare * 100, { decimals: 0 })}</div>
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
            <PostCardList
                posts={account.samples.map(sampleDocToPostCard)}
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
                                {formatPct(a.flaggedPostShare * 100, { decimals: 0 })}
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
    const [activeItem, setActiveItem] = useState<BotEntityItem | null>(null);
    // Cross-page entity deep link ("#bots?entity=<entityId>").
    const [entityParam, setEntityParam] = useDeepLinkParam('entity');
    const { data, loading, error, refetch } = useFetch<BotActivityResponse>(
        () => fetchBotActivity(filters.timeRange),
        [filters.timeRange],
        `bot-activity:${filters.timeRange}`,
    );
    const { data: snapshotStatus } = useFetch<SnapshotStatusResponse>(
        () => fetchSnapshotStatus(),
        [],
        'snapshot-status',
    );

    // Resolve entity= once data lands; unknown entities clear the param.
    // Pre-cutover matched on (kind, key); the current param is a numeric
    // entityId (see EntityHubLinks.tsx), so this matches on entityProfile.entityId.
    useEffect(() => {
        if (!data || !entityParam) return;
        const target = parseEntityParam(entityParam);
        if (target != null) {
            const lists = [data.byOfficial, data.byGeneralPublic];
            for (const list of lists) {
                const item = list.find((it) => it.entityProfile.entityId === target);
                if (item) {
                    setActiveItem(item);
                    return;
                }
            }
        }
        setEntityParam(null);
    }, [data, entityParam, setEntityParam]);

    if (error) {
        return <ErrorState message={error.message} onRetry={refetch} />;
    }

    if (loading) {
        return (
            <div className="flex flex-col gap-6">
                <div className="grid-3">
                    <LoadingCard />
                    <LoadingCard />
                    <LoadingCard />
                </div>
                <LoadingCard />
                <LoadingCard />
            </div>
        );
    }

    // Zero flagged accounts is a legitimate signal (pipeline ran, nothing
    // tripped) — the frame still renders with per-column empty copy; only an
    // absent fetch result hides the whole page.
    if (!data) return <EmptyState title="No bot-activity data available" />;

    const botTickerItems = buildBotTickerItems(data);
    const botRefreshed = formatRefreshedAgo(pipelineRunTimestamp(snapshotStatus));

    return (
        <div className="dashboard-grid">
            <div className="col-span-12">
                <GlobalTicker
                    items={botTickerItems}
                    refreshed={botRefreshed}
                    ariaLabel="Bot detector overview"
                    legend={
                        <MethodPopover
                            title="How to read these numbers"
                            description={
                                'Suspected automation = the share of scored posts our detector flags as likely '
                                + 'automated. Coordination = a 0 (none) to 1 (high) index of timing overlap across '
                                + 'suspected accounts. These are probabilistic leads, not verdicts.'
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
                <BotThreeWayGrid data={data} onOpen={setActiveItem} />
                <p className="card-note">
                    News articles are not bot-scored: articles are not accounts, so
                    an outlet has no automation rate. Bot detection covers social
                    posts only.
                </p>
            </div>

            {activeItem && (
                <BotEntityModal
                    item={activeItem}
                    onClose={() => {
                        setActiveItem(null);
                        if (entityParam) setEntityParam(null);
                    }}
                />
            )}

            <div className="col-span-12">
                <CoordinationSummary data={data.coordinationStats} />
            </div>

            <div className="col-span-12 bot-section-label">
                <div className="eyebrow" style={{ marginBottom: 'var(--space-1)' }}>
                    Stories Bots Appear to Be Pushing
                </div>
                <div className="text-xs text-muted" style={{ lineHeight: 'var(--leading-relaxed)' }}>
                    Each row below is a narrative whose amplifying accounts score as likely bots. Open View
                    details to see the bot-authored posts carrying it.
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

            {/* Behavioral signals — four cards rendered as direct grid children.
                See BehavioralSignalsPanel for per-card span decisions. */}
            <BehavioralSignalsPanel
                accountAgeBuckets={data.accountAgeBuckets}
                behavioralSignals={data.behavioralSignals}
                postingCadenceGrid={data.postingCadenceGrid}
            />

            <div className="col-span-12">
                <FlaggedAccountsCard accounts={data.flaggedAccounts} />
            </div>

            <div className="col-span-12">
                <Card title="Flagged posts">
                    <PostCardList
                        posts={data.flaggedDocs.map(sampleDocToPostCard)}
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
                        rate, text repetition, account age, and coordinated timing.
                    </p>
                    <p className="text-sm">
                        Some real users post in bot-like ways, and some real bots post like humans.
                        Treat flags as <strong>leads, not verdicts</strong>: every signal points at
                        the specific behavior that triggered it, so a reader can audit each call.
                    </p>
                </CollapsibleInfo>
            </div>
        </div>
    );
}

export default BotActivityProfiler;
