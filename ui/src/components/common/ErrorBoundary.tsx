import { Component, type ErrorInfo, type ReactNode } from 'react';
import ErrorState from './ErrorState';

interface ErrorBoundaryProps {
    children: ReactNode;
}

interface ErrorBoundaryState {
    error: Error | null;
}

/**
 * ErrorBoundary - catches render-time throws anywhere below it so a page
 * bug degrades to an ErrorState instead of unmounting the whole tree to a
 * blank screen. Class component on purpose: error boundaries have no hook
 * equivalent. The error + component stack go to console.error only -- the
 * durable ops.error_log sink is backend-side by design (an unauthenticated
 * client beacon would be an abuse surface).
 */
class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
    state: ErrorBoundaryState = { error: null };

    static getDerivedStateFromError(error: Error): ErrorBoundaryState {
        return { error };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error('Render error caught by ErrorBoundary:', error, info.componentStack);
    }

    render() {
        if (this.state.error) {
            return (
                <div style={{ padding: 'var(--space-4)' }}>
                    <ErrorState
                        message="This page hit a rendering error."
                        details={this.state.error.message}
                        onRetry={() => this.setState({ error: null })}
                    />
                </div>
            );
        }
        return this.props.children;
    }
}

export default ErrorBoundary;
