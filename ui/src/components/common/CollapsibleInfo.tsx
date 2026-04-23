import type { ReactNode } from 'react';

interface CollapsibleInfoProps {
    /** Visible summary label. Defaults to "How this page works". */
    summary?: string;
    children: ReactNode;
    /** Optional extra class on the <details>. */
    className?: string;
}

/** Shared `<details>` collapsible used for page-bottom methodology blurbs.
 *  Styled via the `.how-this-works` rules in index.css. */
export function CollapsibleInfo({
    summary = 'How this page works',
    children,
    className,
}: CollapsibleInfoProps) {
    return (
        <details className={`how-this-works${className ? ` ${className}` : ''}`}>
            <summary>{summary}</summary>
            <div className="how-this-works-body">{children}</div>
        </details>
    );
}

export default CollapsibleInfo;
