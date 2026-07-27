import { Card, DefinitionChip, EntityHeader, Modal } from '../../components/common';
import { saturationLevel } from '../../services/glossary';
import { formatPct } from '../../services/format';
import { entityGroupLabel } from './entityGroup';
import type { PropagandaEntityRow } from '../../types';

// --------------------------------------------------------------------------- //
//  PropagandaEntityModal — restored per-entity drill-down (docs/todos/       //
//  ui-feature-restoration.md), now that /propaganda carries byEntity rows    //
//  (analysis/src/api/models/propaganda.py::PropagandaEntityRow). Opens on a  //
//  news outlet / subreddit / author-tier's own footprint. Degraded: that     //
//  row has no per-entity example list (no examples_by_entity field), so this //
//  drops the per-entity evidence section rather than fill it with the       //
//  window-wide examples under one entity's name.                            //
// --------------------------------------------------------------------------- //

export function PropagandaEntityModal({ item, onClose }: { item: PropagandaEntityRow; onClose: () => void }) {
    const flaggedDocs = Math.round(item.flaggedShare * item.docCount);
    return (
        <Modal
            isOpen
            onClose={onClose}
            title={item.displayName}
            subtitle={`${flaggedDocs.toLocaleString()} flagged · ${item.docCount.toLocaleString()} scored`}
        >
            <EntityHeader entity={{ kind: entityGroupLabel(item.group), displayName: item.displayName }} />

            <div className="entity-modal-stats">
                <div>
                    <div className="eyebrow">Flagged share</div>
                    <div className="metric-value">{formatPct(item.flaggedShare * 100)}</div>
                </div>
                <div>
                    <div className="eyebrow">
                        <DefinitionChip entry="mean_score" label="Saturation" />
                    </div>
                    <div className="metric-value">{saturationLevel(item.meanDensity)}</div>
                    <div className="text-xs text-muted">{item.meanDensity.toFixed(2)} on a 0-to-1 scale</div>
                </div>
                <div>
                    <div className="eyebrow">Posts scored</div>
                    <div className="metric-value">{item.docCount.toLocaleString()}</div>
                </div>
            </div>

            <Card>
                <p className="text-sm text-muted" style={{ margin: 0 }}>
                    Per-entity flagged examples aren't broken out in this build's data — see
                    "Flagged examples" on the page for the window's full sample.
                </p>
            </Card>
        </Modal>
    );
}

export default PropagandaEntityModal;
