import { useEffect, useRef, useState } from 'react';
import { GLOSSARY, type GlossaryEntry, type GlossaryKey } from '../../services/glossary';

interface DefinitionChipProps {
    /** Glossary entry to define. */
    entry: GlossaryKey;
    /** Visible label; defaults to the glossary term. */
    label?: string;
}

/**
 * DefinitionChip — a dotted-underline term that opens its glossary
 * definition on click/tap. The touch-and-keyboard-accessible replacement
 * for the title= hover tooltips that phone readers never see. Definitions
 * come from services/glossary so the same term reads identically
 * everywhere.
 */
function DefinitionChip({ entry, label }: DefinitionChipProps) {
    const def: GlossaryEntry = GLOSSARY[entry];
    const [isOpen, setIsOpen] = useState(false);
    const rootRef = useRef<HTMLSpanElement>(null);

    useEffect(() => {
        if (!isOpen) return;
        const onPointerDown = (e: MouseEvent | TouchEvent) => {
            if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
                setIsOpen(false);
            }
        };
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') setIsOpen(false);
        };
        document.addEventListener('mousedown', onPointerDown);
        document.addEventListener('touchstart', onPointerDown);
        document.addEventListener('keydown', onKeyDown);
        return () => {
            document.removeEventListener('mousedown', onPointerDown);
            document.removeEventListener('touchstart', onPointerDown);
            document.removeEventListener('keydown', onKeyDown);
        };
    }, [isOpen]);

    return (
        <span className="definition-chip" ref={rootRef}>
            <button
                type="button"
                className="definition-chip-term"
                onClick={() => setIsOpen((v) => !v)}
                aria-expanded={isOpen}
                aria-label={`Define ${def.term}`}
            >
                {label ?? def.term}
            </button>
            {isOpen && (
                <span className="popover definition-chip-popover" role="note">
                    <span className="popover-title">{def.term}</span>
                    <span className="definition-chip-definition">{def.definition}</span>
                    {def.scale && (
                        <span className="definition-chip-scale">Scale: {def.scale}</span>
                    )}
                </span>
            )}
        </span>
    );
}

export default DefinitionChip;
