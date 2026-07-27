import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const api = {
  getBackendState: vi.fn(), restartBackend: vi.fn(), openLogsDirectory: vi.fn(),
  listWorkspaces: vi.fn(), addWorkspace: vi.fn(), openWorkspaceFolder: vi.fn(),
  listConversations: vi.fn(), createConversation: vi.fn(), loadConversation: vi.fn(),
  getLlmSettings: vi.fn(), switchProvider: vi.fn(), switchEffort: vi.fn(), selectImages: vi.fn(), pasteClipboardImage: vi.fn(),
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

  it('renders conversations for every workspace without selecting a workspace first', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([
      { id: 'workspace-1', name: 'demo-a', workDirectory: '/tmp/demo-a', lastUpdatedAt: '', conversationCount: 1, directoryExists: true },
      { id: 'workspace-2', name: 'demo-b', workDirectory: '/tmp/demo-b', lastUpdatedAt: '', conversationCount: 1, directoryExists: true },
    ])
    api.listConversations.mockImplementation(async (workspaceId: string) => [{
      id: `conversation-${workspaceId}`,
      title: workspaceId === 'workspace-1' ? '项目 A 会话' : '项目 B 会话',
      createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '',
    }])
    render(<App />)

    expect(await screen.findByRole('button', { name: /项目 A 会话/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /项目 B 会话/ })).toBeInTheDocument()
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

  it('updates the selected LLM settings immediately after a successful switch', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([])
    api.getLlmSettings.mockResolvedValue({
      currentProvider: 'glm52',
      currentEffort: 'Medium',
      providers: ['glm52', 'claude'],
      efforts: ['Low', 'Medium', 'High'],
    })
    render(<App />)

    const provider = await screen.findByLabelText('模型提供方')
    const effort = screen.getByLabelText('推理强度')
    fireEvent.change(provider, { target: { value: 'claude' } })
    fireEvent.change(effort, { target: { value: 'High' } })

    await waitFor(() => expect(api.switchProvider).toHaveBeenCalledWith('claude'))
    await waitFor(() => expect(api.switchEffort).toHaveBeenCalledWith('High'))
    expect(provider).toHaveValue('claude')
    expect(effort).toHaveValue('High')
  })

  it('keeps an optimistic user message visible when switching away from an active conversation', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 2, directoryExists: true }])
    api.listConversations.mockResolvedValue([
      { id: 'conversation-1', title: '运行中的会话', createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' },
      { id: 'conversation-2', title: '另一个会话', createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' },
    ])
    api.loadConversation.mockImplementation(async (conversationId: string) => ({ id: conversationId, tokenCount: 0, messages: [] }))
    api.startRun.mockResolvedValue({ runId: 'run-1', status: 'running', eventsUrl: '' })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
    fireEvent.click(await screen.findByRole('button', { name: /运行中的会话/ }))
    fireEvent.change(screen.getByLabelText('输入消息'), { target: { value: '切换后仍需显示' } })
    fireEvent.click(screen.getByRole('button', { name: '发送消息' }))
    expect(await screen.findByText('切换后仍需显示')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /另一个会话/ }))
    expect(await screen.findByRole('heading', { name: '另一个会话' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /运行中的会话/ }))

    expect(await screen.findByText('切换后仍需显示')).toBeInTheDocument()
    expect(screen.getAllByText('切换后仍需显示')).toHaveLength(1)
  })

  it('adds a pasted image attachment and sends it without draft text', async () => {
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 1, directoryExists: true }])
    api.listConversations.mockResolvedValue([{ id: 'conversation-1', title: '图片会话', createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' }])
    api.loadConversation.mockResolvedValue({ id: 'conversation-1', tokenCount: 0, messages: [] })
    api.pasteClipboardImage.mockResolvedValue({ path: '/tmp/demo/tmp/clipboard-images/clipboard.png', mediaType: 'image/png' })
    api.startRun.mockResolvedValue({ runId: 'run-1', status: 'running', eventsUrl: '' })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
    fireEvent.click(await screen.findByRole('button', { name: /图片会话/ }))
    fireEvent.paste(screen.getByLabelText('输入消息'), {
      clipboardData: { items: [{ type: 'image/png' }] },
    })

    expect(await screen.findByText('clipboard.png')).toBeInTheDocument()
    expect(api.pasteClipboardImage).toHaveBeenCalledWith('/tmp/demo')
    fireEvent.click(screen.getByRole('button', { name: '发送消息' }))
    await waitFor(() => expect(api.startRun).toHaveBeenCalledWith({
      conversationId: 'conversation-1',
      content: '[图片]',
      attachments: [{ path: '/tmp/demo/tmp/clipboard-images/clipboard.png', mediaType: 'image/png' }],
    }))
  })

  it('queues messages during a run and starts them in FIFO order after completion', async () => {
    const listeners = new Map<string, (event: any) => void>()
    api.getBackendState.mockResolvedValue({ connected: true, error: null })
    api.listWorkspaces.mockResolvedValue([{ id: 'workspace-1', name: 'demo', workDirectory: '/tmp/demo', lastUpdatedAt: '', conversationCount: 1, directoryExists: true }])
    api.listConversations.mockResolvedValue([{ id: 'conversation-1', title: '队列会话', createdAt: '', updatedAt: '', messageCount: 0, tokenCount: 0, lastUsageTokenCount: 0, lastUserMessagePreview: '' }])
    api.loadConversation.mockResolvedValue({ id: 'conversation-1', tokenCount: 0, messages: [] })
    api.startRun.mockResolvedValueOnce({ runId: 'run-1', status: 'running', eventsUrl: '' }).mockResolvedValueOnce({ runId: 'run-2', status: 'running', eventsUrl: '' })
    ;(api.onRunEvent as any).mockImplementation((runId: string, listener: (event: any) => void) => { listeners.set(runId, listener); return () => {} })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: '展开 demo' }))
    fireEvent.click(await screen.findByRole('button', { name: /队列会话/ }))
    fireEvent.change(screen.getByLabelText('输入消息'), { target: { value: '第一条任务' } })
    fireEvent.click(screen.getByRole('button', { name: '发送消息' }))
    await waitFor(() => expect(api.startRun).toHaveBeenCalledTimes(1))

    fireEvent.change(screen.getByLabelText('输入消息'), { target: { value: '第二条任务' } })
    fireEvent.click(screen.getByRole('button', { name: '发送消息' }))
    expect(await screen.findByText('排队消息（1）')).toBeInTheDocument()
    expect(screen.getByText('第二条任务')).toBeInTheDocument()
    expect(api.startRun).toHaveBeenCalledTimes(1)

    act(() => listeners.get('run-1')?.({ runId: 'run-1', sequence: 1, eventType: 'run.completed', content: null, approvalId: null }))
    await waitFor(() => expect(api.startRun).toHaveBeenNthCalledWith(2, {
      conversationId: 'conversation-1', content: '第二条任务', attachments: [],
    }))
    expect(screen.queryByText('排队消息（1）')).not.toBeInTheDocument()
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
      onEvent?.({ runId: 'run-1', sequence: 5, eventType: 'message.delta', content: '## 已完成\n\n- 支持 **Markdown**\n- 不重复拼接', approvalId: null })
      onEvent?.({ runId: 'run-1', sequence: 6, eventType: 'approval.required', content: null, approvalId: 'approval-1' })
    })

    expect(screen.getByRole('button', { name: /读取 src\/App\.tsx/ })).toHaveAttribute('aria-expanded', 'false')
    expect(await screen.findByRole('heading', { name: '已完成' })).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { name: '已完成' })).toHaveLength(1)
    expect(screen.getByText('不重复拼接')).toBeInTheDocument()
    expect(screen.getByText('等待工具授权')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '允许执行' }))
    await waitFor(() => expect(api.resolveApproval).toHaveBeenCalledWith('run-1', 'approval-1', true))

    act(() => {
      onEvent?.({ runId: 'run-1', sequence: 7, eventType: 'run.completed', content: null, approvalId: null })
    })
    await waitFor(() => expect(screen.queryByRole('button', { name: '取消运行' })).not.toBeInTheDocument())
    expect(screen.queryByText('正在启动任务')).not.toBeInTheDocument()
  })
})
