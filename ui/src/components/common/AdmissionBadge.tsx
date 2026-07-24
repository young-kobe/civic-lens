import type { AdmissionClass } from '../../types';

// --------------------------------------------------------------------------- //
//  AdmissionBadge — visually distinguishes 'sampled' discourse from          //
//  'official_record' posts (public-record posts by tracked officials, NOT    //
//  samples — owner decision 2026-07-24: never apply sample/proxy            //
//  disclaimers to those).                                                    //
// --------------------------------------------------------------------------- //

export function AdmissionBadge({ admissionClass }: { admissionClass: AdmissionClass }) {
    if (admissionClass === 'official_record') {
        return (
            <span
                className="badge badge-accent"
                title="A public-record post by a tracked official — not a sample; the sample/proxy disclaimers on this page don't apply to it."
            >
                Official record
            </span>
        );
    }
    return (
        <span
            className="badge badge-neutral"
            title="Sampled discourse — one of the posts we happened to collect, not a complete record."
        >
            Sampled
        </span>
    );
}

export default AdmissionBadge;
