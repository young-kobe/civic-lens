import { useState, useRef, useEffect, ReactNode } from 'react';

interface MethodPopoverProps {
    title?: string;
    description?: string;
    limitations?: string[];
    children?: ReactNode;
}

/**
 * MethodPopover - Expandable explanation of methodology.
 */
function MethodPopover({
    title = 'Methodology',
    description,
    limitations,
    children
}: MethodPopoverProps) {
    const [isOpen, setIsOpen] = useState(false);
    const popoverRef = useRef<HTMLDivElement>(null);

    // Close on outside click
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (popoverRef.current && !popoverRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    return (
        <div style={{ position: 'relative', display: 'inline-flex' }} ref={popoverRef}>
            <button
                className="tooltip-trigger text-muted text-xs"
                onClick={() => setIsOpen(!isOpen)}
                aria-expanded={isOpen}
            >
                <svg
                    className="tooltip-icon"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    style={{ width: '14px', height: '14px' }}
                >
                    <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a.75.75 0 000 1.5h.253a.25.25 0 01.244.304l-.459 2.066A1.75 1.75 0 0010.747 15H11a.75.75 0 000-1.5h-.253a.25.25 0 01-.244-.304l.459-2.066A1.75 1.75 0 009.253 9H9z" clipRule="evenodd" />
                </svg>
                <span>Method</span>
            </button>

            {isOpen && (
                <div
                    className="popover"
                    style={{
                        top: '100%',
                        right: 0,
                        marginTop: '8px',
                        minWidth: '280px'
                    }}
                >
                    <div className="popover-title">{title}</div>

                    {description && (
                        <p className="text-sm mb-3">{description}</p>
                    )}

                    {limitations && limitations.length > 0 && (
                        <div className="mt-3">
                            <div className="text-xs font-medium text-muted mb-1">Known Limitations</div>
                            <ul style={{ margin: 0, paddingLeft: '16px' }}>
                                {limitations.map((limitation, i) => (
                                    <li key={i} className="text-xs text-muted">{limitation}</li>
                                ))}
                            </ul>
                        </div>
                    )}

                    {children}
                </div>
            )}
        </div>
    );
}

export default MethodPopover;
