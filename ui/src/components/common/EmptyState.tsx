

interface EmptyStateProps {
    title?: string;
    description?: string;
    action?: string;
    onAction?: () => void;
}

/**
 * EmptyState - Minimal empty state with icon and message.
 */
function EmptyState({
    title = 'No data available',
    description,
    action,
    onAction
}: EmptyStateProps) {
    return (
        <div className="empty-state">
            <svg
                className="empty-state-icon"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.5"
            >
                <path strokeLinecap="round" strokeLinejoin="round" d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4" />
            </svg>
            <h3 className="empty-state-title">{title}</h3>
            {description && (
                <p className="empty-state-description">{description}</p>
            )}
            {action && onAction && (
                <button className="btn btn-secondary mt-4" onClick={onAction}>
                    {action}
                </button>
            )}
        </div>
    );
}

export default EmptyState;
