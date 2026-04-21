import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface ModalProps {
    isOpen: boolean;
    onClose: () => void;
    title: string;
    subtitle?: string;
    /** Optional accent color applied to the left border + title — used by
     *  callers that want to signal which bucket / segment the modal belongs to. */
    accentColor?: string;
    /** Control maximum width; defaults to 860px so a docs table can breathe. */
    maxWidth?: number;
    children: ReactNode;
}

/**
 * Centered dialog rendered into a portal. Escape and backdrop click both
 * close it; body scroll is locked while open so the page doesn't shift.
 *
 * Accessibility notes:
 * - role="dialog" + aria-modal="true" on the surface
 * - aria-labelledby points at the title; aria-describedby at the subtitle
 * - Initial focus lands on the close button so keyboard users have an
 *   obvious exit; content inside remains tab-navigable.
 */
export function Modal({
    isOpen,
    onClose,
    title,
    subtitle,
    accentColor,
    maxWidth = 860,
    children,
}: ModalProps) {
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!isOpen) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') onClose();
        };
        document.addEventListener('keydown', onKeyDown);
        const prevOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        closeRef.current?.focus();
        return () => {
            document.removeEventListener('keydown', onKeyDown);
            document.body.style.overflow = prevOverflow;
        };
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    const titleId = 'modal-title';
    const subtitleId = subtitle ? 'modal-subtitle' : undefined;

    return createPortal(
        <div
            className="modal-backdrop"
            onClick={onClose}
            role="presentation"
        >
            <div
                className="modal-surface"
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={subtitleId}
                onClick={(e) => e.stopPropagation()}
                style={{
                    maxWidth,
                    borderLeftColor: accentColor ?? 'transparent',
                }}
            >
                <header className="modal-header">
                    <div style={{ minWidth: 0 }}>
                        <div className="eyebrow" style={{ color: accentColor ?? 'var(--neutral-500)' }} id={titleId}>
                            {title}
                        </div>
                        {subtitle && (
                            <div
                                id={subtitleId}
                                className="text-xs text-muted mt-1"
                                style={{ fontVariantNumeric: 'tabular-nums' }}
                            >
                                {subtitle}
                            </div>
                        )}
                    </div>
                    <button
                        ref={closeRef}
                        type="button"
                        className="modal-close"
                        onClick={onClose}
                        aria-label="Close"
                    >
                        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden stroke="currentColor" strokeWidth="2" fill="none" strokeLinecap="round">
                            <path d="M6 6l12 12M18 6L6 18" />
                        </svg>
                    </button>
                </header>
                <div className="modal-body">
                    {children}
                </div>
            </div>
        </div>,
        document.body,
    );
}

export default Modal;
