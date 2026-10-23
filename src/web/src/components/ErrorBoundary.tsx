import { Component, type ErrorInfo, type ReactNode } from 'react'

type Props = { children: ReactNode; label: string }
type State = { error: Error | null; attempt: number }

/**
 * Keeps one broken tab from blanking the whole app: the navigation stays usable and the user can retry the tab.
 * A tab that failed to download (offline, or a new release replaced its file) lands here too.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null, attempt: 0 }

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[${this.props.label}] failed to render`, error, info.componentStack)
  }

  render() {
    if (this.state.error) {
      return (
        <section className="tab-error" role="alert">
          <h2>{this.props.label} could not be shown</h2>
          <p>Something went wrong while loading or drawing this page. Other pages are not affected.</p>
          <p className="tab-error-detail">{this.state.error.message}</p>
          <button type="button" onClick={() => this.setState((state) => ({ error: null, attempt: state.attempt + 1 }))}>
            Try again
          </button>
        </section>
      )
    }
    // `attempt` in the key remounts the children on retry instead of re-rendering the failed tree.
    return <div key={this.state.attempt} style={{ display: 'contents' }}>{this.props.children}</div>
  }
}
