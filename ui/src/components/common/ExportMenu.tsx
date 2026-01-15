import React, { useState, useRef, useEffect } from 'react';

type ExportFormat = 'png' | 'pdf' | 'json' | 'csv';

interface ExportOption {
    id: ExportFormat;
    label: string;
    icon: string;
}

interface ExportMenuProps {
    onExport?: (format: ExportFormat) => void;
}

/**
 * ExportMenu - Dropdown menu for export options.
 */
function ExportMenu({ onExport }: ExportMenuProps) {
    const [isOpen, setIsOpen] = useState(false);
    const menuRef = useRef<HTMLDivElement>(null);

    const exportOptions: ExportOption[] = [
        { id: 'png', label: 'Export as PNG', icon: 'image' },
        { id: 'pdf', label: 'Export as PDF', icon: 'document' },
        { id: 'json', label: 'Export data (JSON)', icon: 'code' },
        { id: 'csv', label: 'Export data (CSV)', icon: 'table' },
    ];

    // Close on outside click
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    const handleExport = (format: ExportFormat) => {
        setIsOpen(false);
        if (onExport) {
            onExport(format);
        }
    };

    return (
        <div style={{ position: 'relative' }} ref={menuRef}>
            <button
                className="btn btn-secondary btn-sm"
                onClick={() => setIsOpen(!isOpen)}
                aria-expanded={isOpen}
                aria-haspopup="menu"
            >
                <svg
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    style={{ width: '16px', height: '16px' }}
                >
                    <path d="M10.75 2.75a.75.75 0 00-1.5 0v8.614L6.295 8.235a.75.75 0 10-1.09 1.03l4.25 4.5a.75.75 0 001.09 0l4.25-4.5a.75.75 0 00-1.09-1.03l-2.955 3.129V2.75z" />
                    <path d="M3.5 12.75a.75.75 0 00-1.5 0v2.5A2.75 2.75 0 004.75 18h10.5A2.75 2.75 0 0018 15.25v-2.5a.75.75 0 00-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5z" />
                </svg>
                Export
            </button>

            {isOpen && (
                <div
                    className="popover"
                    role="menu"
                    style={{
                        top: '100%',
                        right: 0,
                        marginTop: '4px',
                        padding: 'var(--space-2)',
                        minWidth: '180px'
                    }}
                >
                    {exportOptions.map((option) => (
                        <button
                            key={option.id}
                            role="menuitem"
                            className="btn btn-ghost"
                            style={{
                                width: '100%',
                                justifyContent: 'flex-start',
                                padding: 'var(--space-2) var(--space-3)'
                            }}
                            onClick={() => handleExport(option.id)}
                        >
                            {option.label}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}

export default ExportMenu;
