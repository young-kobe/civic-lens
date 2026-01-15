import React from 'react';

interface LoadingSkeletonProps {
    width?: string;
    height?: string;
    className?: string;
}

/**
 * LoadingSkeleton - Base skeleton loading component.
 */
export function LoadingSkeleton({ width = '100%', height = '1em', className = '' }: LoadingSkeletonProps) {
    return (
        <div
            className={`skeleton ${className}`}
            style={{ width, height }}
            aria-hidden="true"
        />
    );
}

export function LoadingCard() {
    return (
        <div className="card">
            <div className="skeleton skeleton-title" />
            <div className="skeleton skeleton-text" style={{ width: '80%' }} />
            <div className="skeleton skeleton-text" style={{ width: '60%' }} />
            <div className="skeleton" style={{ height: '120px', marginTop: 'var(--space-4)' }} />
        </div>
    );
}

export function LoadingMetric() {
    return (
        <div className="card">
            <div className="skeleton skeleton-text" style={{ width: '60%' }} />
            <div className="skeleton skeleton-metric" />
        </div>
    );
}

interface LoadingTableProps {
    rows?: number;
}

export function LoadingTable({ rows = 5 }: LoadingTableProps) {
    return (
        <div className="card">
            <div className="skeleton skeleton-title" />
            {Array.from({ length: rows }).map((_, i) => (
                <div key={i} className="flex gap-4 mt-3">
                    <div className="skeleton skeleton-text" style={{ width: '30%' }} />
                    <div className="skeleton skeleton-text" style={{ width: '20%' }} />
                    <div className="skeleton skeleton-text" style={{ width: '40%' }} />
                </div>
            ))}
        </div>
    );
}

export function LoadingChart() {
    return (
        <div className="card">
            <div className="skeleton skeleton-title" />
            <div className="skeleton" style={{ height: '200px', marginTop: 'var(--space-4)' }} />
        </div>
    );
}

type LoadingType = 'card' | 'metric' | 'table' | 'chart';

interface LoadingStateProps {
    type?: LoadingType;
    count?: number;
}

function LoadingState({ type = 'card', count = 1 }: LoadingStateProps) {
    const componentMap: Record<LoadingType, React.FC<Record<string, unknown>>> = {
        card: LoadingCard,
        metric: LoadingMetric,
        table: LoadingTable,
        chart: LoadingChart,
    };

    const Component = componentMap[type];

    return (
        <>
            {Array.from({ length: count }).map((_, i) => (
                <Component key={i} />
            ))}
        </>
    );
}

export default LoadingState;
