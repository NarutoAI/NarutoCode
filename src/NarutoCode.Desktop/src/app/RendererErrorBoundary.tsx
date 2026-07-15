import { Component, type ErrorInfo, type ReactNode } from 'react'

interface RendererErrorBoundaryProps {
  children: ReactNode
}

interface RendererErrorBoundaryState {
  error: Error | null
}

/**
 * 防止单个 Renderer 异常卸载整个 Electron 窗口。
 * 出错时保留错误信息并提供一次完整重载入口，便于恢复工作台。
 */
export class RendererErrorBoundary extends Component<RendererErrorBoundaryProps, RendererErrorBoundaryState> {
  public state: RendererErrorBoundaryState = { error: null }

  /** 将下级渲染异常转换为可恢复的错误状态。 */
  public static getDerivedStateFromError(error: Error): RendererErrorBoundaryState {
    return { error }
  }

  /** 将组件堆栈输出到开发者控制台，方便定位后续异常。 */
  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('NarutoCode Renderer crashed.', error, errorInfo.componentStack)
  }

  public render(): ReactNode {
    if (!this.state.error) {
      return this.props.children
    }

    return <main className="renderer-error"><section><h1>界面渲染出现异常</h1><p>{this.state.error.message || '未知错误。'}</p><button type="button" onClick={() => window.location.reload()}>重新加载界面</button></section></main>
  }
}
