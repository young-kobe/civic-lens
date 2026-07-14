import React, { ReactNode } from 'react';

interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
    title?: string;
    /** Plain string or a node (e.g. a DefinitionChip inside the deck). */
    subtitle?: ReactNode;
    children: ReactNode;
    note?: string;
    className?: string;
    headerActions?: ReactNode;
}

/**
 * Card - Universal card container with consistent structure.
 * Pattern: Title | Subtitle | KPI | Visualization | Note
 */
function Card({
    title,
    subtitle,
    children,
    note,
    className = '',
    headerActions,
    ...props
}: CardProps) {
    return (
        <div className={`card ${className}`} {...props}>
            {(title || subtitle || headerActions) && (
                <div className="card-header flex items-center justify-between">
                    <div>
                        {title && <h3 className="card-title">{title}</h3>}
                        {subtitle && <p className="card-subtitle">{subtitle}</p>}
                    </div>
                    {headerActions && <div className="flex gap-2">{headerActions}</div>}
                </div>
            )}
            <div className="card-body">
                {children}
            </div>
            {note && (
                <div className="card-note">
                    {note}
                </div>
            )}
        </div>
    );
}

export default Card;
