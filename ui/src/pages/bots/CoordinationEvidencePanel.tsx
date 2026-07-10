import { Card, DefinitionChip, MethodPopover } from '../../components/common';
import { formatCount, formatPct } from '../../services/format';
import { coordinationLevel } from '../../services/glossary';
import { COLORS } from '../../theme';
import type { BotOverview } from '../../types';

// --------------------------------------------------------------------------- //
//  CoordinationEvidencePanel — the Bot Detector's signature strip: the        //
//  detector's chain of evidence as a connected flow.                          //
//                                                                             //
//     posts scanned → flagged as likely automated → posting in lockstep →    //
//     linking mostly to: <domains>                                            //
//                                                                             //
//  Replaces the BotOverviewMetrics card row, which repeated the ticker's      //
//  numbers in a second shape without adding a reading.                        //
// --------------------------------------------------------------------------- //

interface CoordinationEvidencePanelProps {
    overview: BotOverview;
    /** Total docs scanned across tiers — derived by the page from the
     *  entity rollups so the funnel's first stage is honest. Null when the
     *  snapshot predates per-entity rollups. */
    totalScanned: number | null;
}

function friendlyCluster(cluster: string): string {
    return cluster.replace(/^www\./i, '');
}

export function CoordinationEvidencePanel({ overview, totalScanned }: CoordinationEvidencePanelProps) {
    const rate = overview.suspectedAutomationRate;
    const level = coordinationLevel(overview.coordinationIndex);
    const levelColor = level === 'high'
        ? COLORS.negative
        : level === 'moderate'
            ? COLORS.warning
            : COLORS.positive;
    const clusters = overview.topClusters.slice(0, 4);

    return (
        <Card
            title="The chain of evidence"
            subtitle="How the detector narrows the sample down to suspect activity. Each stage is a lead, not a verdict."
            headerActions={
                <MethodPopover
                    description={
                        'The detector scores every scanned post from behavioral signals '
                        + '(posting rate, text repetition, account age, coordinated timing). '
                        + 'Flagged posts are then checked for lockstep behavior — identical '
                        + 'text, synchronized timing — and for shared link targets.'
                    }
                    limitations={[
                        'Some real users post in bot-like ways, and some real bots post like humans.',
                        'Coordination can be organic (a hashtag campaign) as well as automated.',
                    ]}
                />
            }
        >
            <div className="evidence-funnel">
                <div className="evidence-funnel-stage">
                    <span className="evidence-funnel-value">
                        {totalScanned != null ? formatCount(totalScanned) : '—'}
                    </span>
                    <span className="evidence-funnel-label">posts scanned</span>
                </div>
                <span className="evidence-funnel-arrow" aria-hidden />
                <div className="evidence-funnel-stage">
                    <span
                        className="evidence-funnel-value"
                        style={{ color: rate > 10 ? COLORS.negative : rate > 3 ? COLORS.warning : undefined }}
                    >
                        {formatCount(overview.totalFlaggedPosts)}
                    </span>
                    <span className="evidence-funnel-label">
                        flagged as likely automated ({formatPct(rate, { decimals: 0 })})
                    </span>
                </div>
                <span className="evidence-funnel-arrow" aria-hidden />
                <div className="evidence-funnel-stage">
                    <span className="evidence-funnel-value" style={{ color: levelColor }}>
                        {level}
                    </span>
                    <span className="evidence-funnel-label">
                        <DefinitionChip entry="coordination" label="coordination" />
                        {' '}· {overview.coordinationIndex.toFixed(2)} / 1
                    </span>
                </div>
                {clusters.length > 0 && (
                    <>
                        <span className="evidence-funnel-arrow" aria-hidden />
                        <div className="evidence-funnel-stage evidence-funnel-stage-domains">
                            <span className="evidence-funnel-domains">
                                {clusters.map((c) => (
                                    <span key={c} className="badge badge-warning">{friendlyCluster(c)}</span>
                                ))}
                            </span>
                            <span className="evidence-funnel-label">
                                domains flagged posts link to most
                            </span>
                        </div>
                    </>
                )}
            </div>
        </Card>
    );
}

export default CoordinationEvidencePanel;
