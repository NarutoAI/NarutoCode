import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const api = {
  getBackendState: vi.fn(), restartBackend: vi.fn(), openLogsDirectory: vi.fn(),
  listWorkspaces: vi.fn(), addWorkspace: vi.fn(), openWorkspaceFolder: vi.fn(),
  listConversations: vi.fn(), createConversation: vi.fn(), loadConversation: vi.fn(),
  getLlmSettings: vi.fn(), switchProvider: vi.fn(), switchEffort: vi.fn(), selectImages: vi.fn(),
  startRun: vi.fn(), resolveApproval: vi.fn(), cancelRun: vi.fn(), onRunEvent: vi.fn(() => () => {}),
}

beforeEach(() => {
  vi.clearAllMocks()
  Object.defineProperty(window, 'narutoCode', { configurable: true, value: api })
  api.getLlmSettings.mockResolvedValue({ currentProvider: 'test', currentEffort: 'Low', providers: ['test'], efforts: ['Low'] })
})

describe('App', () => {
  it('renders the workspace entry point and add workspace control', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([])
    render(<App />)
    expect(await screen.findByText('把代码任务放进一个项目')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '新建对话' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '添加项目' })).toBeInTheDocument()
  })

  it('renders startup retry and logs controls when backend cannot start', async () => {
    api.getBackendState.mockResolvedValue({ connected: false, error: '启动超时' })
    api.restartBackend.mockResolvedValue({ connected: false, error: '启动超时' })
    render(<App />)
    expect(await screen.findByText('后端暂时不可用')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '查看日志' }))
    await waitFor(() => expect(api.openLogsDirectory).toHaveBeenCalledOnce())
  })

  it('does not treat the scroll operation result as an effect cleanup function', async () => {
    const originalScrollIntoView = HTMLElement.prototype.scrollIntoView
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: vi.fn(() => ({ destroy: false })),
    })
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 1, directoryExists: true }])
    api.listConversations.mockResolvedValue([{ id: 'conversation-1', title: '切换会话', createdAt: '', updatedAt: '', messageCount: 1, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' }])
    api.loadConversation.mockResolvedValue({ id: 'conversation-1', tokenCount: 0, messages: [] })

    try {
      render(<App />)
      fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
      fireEvent.click(await screen.findByRole('button', { name: /切换会话/ }))
      expect(await screen.findByRole('heading', { name: '切换会话' })).toBeInTheDocument()
      expect(screen.queryByText('界面渲染出现异常')).not.toBeInTheDocument()
    } finally {
      Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
        configurable: true,
        value: originalScrollIntoView,
      })
    }
  })

  it('normalizes malformed historic messages instead of blanking the renderer', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 1, directoryExists: true }])
    api.listConversations.mockResolvedValue([{ id: 'conversation-1', title: '旧会话', createdAt: '', updatedAt: '', messageCount: 1, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' }])
    api.loadConversation.mockResolvedValue({ id: 'conversation-1', tokenCount: 0, messages: [null, { role: null, content: null, createdAt: null, attachments: null }] })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
    fireEvent.click(await screen.findByRole('button', { name: /旧会话/ }))

    expect(await screen.findByText('NarutoCode')).toBeInTheDocument()
    expect(screen.getByLabelText('输入消息')).toBeInTheDocument()
  })

  it('renders streaming markdown and exposes thinking, tool, and approval controls', async () => {
    let onEvent: ((event: any) => void) | undefined
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 1, directoryExists: true }])
    api.listConversations.mockResolvedValue([{ id: 'conversation-1', title: '实现功能', createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' }])
    api.loadConversation.mockResolvedValue({ id: 'conversation-1', tokenCount: 0, messages: [] })
    api.startRun.mockResolvedValue({ runId: 'run-1', status: 'running', eventsUrl: '' })
    ;(api.onRunEvent as any).mockImplementation((_runId: string, listener: (event: any) => void) => { onEvent = listener; return () => {} })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
    fireEvent.click(await screen.findByRole('button', { name: /实现功能/ }))
    fireEvent.change(screen.getByLabelText('输入消息'), { target: { value: '请实现列表' } })
    fireEvent.click(screen.getByRole('button', { name: '发送消息' }))
    await waitFor(() => expect(onEvent).toBeTypeOf('function'))

    act(() => {
      onEvent?.({ runId: 'run-1', sequence: 1, eventType: 'thinking.delta', content: '分析需求', approvalId: null })
    })
    expect(screen.getByRole('button', { name: /思考过程/ })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('分析需求')).toBeInTheDocument()

    act(() => {
      onEvent?.({ runId: 'run-1', sequence: 2, eventType: 'tool.started', content: '读取 src/App.tsx', approvalId: null })
    })
    expect(screen.getByRole('button', { name: /思考过程/ })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: /读取 src\/App\.tsx/ })).toHaveAttribute('aria-expanded', 'true')

    act(() => {
      onEvent?.({ runId: 'run-1', sequence: 3, eventType: 'tool.completed', content: null, approvalId: null })
      onEvent?.({ runId: 'run-1', sequence: 4, eventType: 'message.delta', content: '## 已完成\n\n- 支持 **Markdown**', approvalId: null })
      onEvent?.({ runId: 'run-1', sequence: 5, eventType: 'approval.required', content: null, approvalId: 'approval-1' })
    })

    expect(screen.getByRole('button', { name: /读取 src\/App\.tsx/ })).toHaveAttribute('aria-expanded', 'false')
    expect(await screen.findByRole('heading', { name: '已完成' })).toBeInTheDocument()
    expect(screen.getByText('等待工具授权')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '允许执行' }))
    await waitFor(() => expect(api.resolveApproval).toHaveBeenCalledWith('run-1', 'approval-1', true))

    act(() => {
      onEvent?.({ runId: 'run-1', sequence: 6, eventType: 'run.completed', content: null, approvalId: null })
    })
    await waitFor(() => expect(screen.queryByRole('button', { name: '取消运行' })).not.toBeInTheDocument())
    expect(screen.queryByText('正在启动任务')).not.toBeInTheDocument()
  })
})
