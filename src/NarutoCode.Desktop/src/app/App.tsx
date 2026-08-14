import { CalendarDotsIcon as CalendarDots } from '@phosphor-icons/react/dist/csr/CalendarDots'
import { CodeIcon as Code } from '@phosphor-icons/react/dist/csr/Code'
import { DotsThreeIcon as DotsThree } from '@phosphor-icons/react/dist/csr/DotsThree'
import { FileCodeIcon as FileCode } from '@phosphor-icons/react/dist/csr/FileCode'
import { FolderOpenIcon as FolderOpen } from '@phosphor-icons/react/dist/csr/FolderOpen'
import { GearIcon as Gear } from '@phosphor-icons/react/dist/csr/Gear'
import { MagnifyingGlassIcon as MagnifyingGlass } from '@phosphor-icons/react/dist/csr/MagnifyingGlass'
import { PenIcon as Pen } from '@phosphor-icons/react/dist/csr/Pen'
import { PlayIcon as Play } from '@phosphor-icons/react/dist/csr/Play'
import { PlugIcon as Plug } from '@phosphor-icons/react/dist/csr/Plug'
import { PlusIcon as Plus } from '@phosphor-icons/react/dist/csr/Plus'
import { SparkleIcon as Sparkle } from '@phosphor-icons/react/dist/csr/Sparkle'
import { TerminalWindowIcon as TerminalWindow } from '@phosphor-icons/react/dist/csr/TerminalWindow'
import { useEffect, useMemo, useRef, useState } from 'react'
import { ActivityBlock, type LiveBlock } from './components/ActivityBlocks'
import { Composer } from './components/Composer'
import { MarkdownContent } from './components/MarkdownContent'
import type {
  Attachment,
  BackendState,
  ConversationHistory,
  ConversationSummary,
  LlmSettings,
  RunEvent,
  StartRunResponse,
  WorkspaceSummary,
} from '../shared/contracts'

function StartupError({ state, retry }: { state: BackendState; retry: () => Promise<void> }) {
  return <main className="main"><section className="empty-state"><div className="startup-error"><Sparkle size={28} weight="fill" /><h1>后端暂时不可用</h1><p>{state.error}</p><div className="approval-actions"><button className="button approve" type="button" onClick={() => void retry()}>重新连接</button><button className="button quiet" type="button" onClick={() => void window.narutoCode.openLogsDirectory()}>查看日志</button></div></div></section></main>
}

function workspaceName(workDirectory: string) {
  return workDirectory.split('/').filter(Boolean).at(-1) ?? workDirectory
}

/** 显示当前 Agent 运行、变更和终端上下文的侧栏。 */
function RunContextPanel({
  workspace,
  conversation,
  blocks,
  activeRun,
}: {
  workspace: WorkspaceSummary | null
  conversation: ConversationSummary | null
  blocks: LiveBlock[]
  activeRun: StartRunResponse | null
}) {
  const completed = blocks.filter(block => block.status === 'completed').length
  const running = blocks.find(block => block.status === 'running')
  const files = [
    { name: 'Conversation.cs', detail: '+ ProjectId', tone: 'added' },
    { name: 'ConversationRepository.cs', detail: running ? '执行中' : '+ 查询', tone: 'added' },
    { name: 'TuiChatApplication.cs', detail: conversation ? '已关联' : '待选择', tone: 'muted' },
  ]

  return <aside className="run-context" aria-label="运行上下文">
    <header className="run-context-head"><span>运行上下文</span><button type="button" aria-label="更多运行上下文"><DotsThree size={16} weight="bold" /></button></header>
    <section className="run-status-card">
      <div className="run-status-title"><div><span className={activeRun ? 'run-led active' : 'run-led'} />{activeRun ? 'Agent 正在执行' : '准备执行任务'}</div><strong>{activeRun ? '运行中' : '就绪'}</strong></div>
      <div className="run-progress" aria-label={`当前已完成 ${completed} 个活动块`}><i className={completed > 0 ? 'done' : ''} /><i className={completed > 1 ? 'done' : activeRun ? 'active' : ''} /><i className={completed > 2 ? 'done' : ''} /><i /></div>
      <p>{workspace ? `${workspace.name} · ${conversation?.title ?? '未选择会话'}` : '选择项目和会话后，Agent 运行状态会显示在这里。'}</p>
    </section>
    <section className="context-changes">
      <h2>本轮上下文</h2>
      {files.map(file => <article className="context-file" key={file.name}><div className="context-file-head"><FileCode size={14} /><span>{file.name}</span><b className={file.tone}>{file.detail}</b></div><code>{file.name === 'Conversation.cs' ? 'ProjectId → Projects.Id' : file.name === 'ConversationRepository.cs' ? '项目 → 会话查询链' : '项目入口 → 会话选择器'}</code></article>)}
      <article className="context-terminal"><div><TerminalWindow size={14} />TERMINAL</div><pre>{activeRun ? '$ 等待工具流输出…' : '$ 等待运行\n✓ Renderer 已连接\n✓ 项目上下文可用'}</pre></article>
    </section>
  </aside>
}

/**
 * 将历史接口的运行时 JSON 规范化为可安全渲染的数据。
 * 旧版本持久化数据可能包含空字段，不能让单条消息卸载整个 Renderer。
 */
function normalizeConversationHistory(value: ConversationHistory): ConversationHistory {
  const rawHistory = value as unknown as { id?: unknown; tokenCount?: unknown; messages?: unknown }
  const rawMessages = Array.isArray(rawHistory.messages) ? rawHistory.messages : []

  return {
    id: typeof rawHistory.id === 'string' ? rawHistory.id : '',
    tokenCount: typeof rawHistory.tokenCount === 'number' ? rawHistory.tokenCount : 0,
    messages: rawMessages.map((item, index) => {
      const message = item && typeof item === 'object'
        ? item as Partial<ConversationHistory['messages'][number]>
        : {}
      return {
        role: typeof message.role === 'string' ? message.role : 'assistant',
        messageType: typeof message.messageType === 'string' ? message.messageType : 'Unknown',
        content: typeof message.content === 'string' ? message.content : '',
        approvalContent: typeof message.approvalContent === 'string' ? message.approvalContent : '',
        createdAt: typeof message.createdAt === 'string' ? message.createdAt : `legacy-${index}`,
        attachments: Array.isArray(message.attachments) ? message.attachments : [],
      }
    }),
  }
}

/** 等待当前 Run 完成后发送的用户消息。 */
interface QueuedUserMessage {
  content: string
  attachments: Attachment[]
  createdAt: string
}

/** 单个会话的运行时状态，SSE 事件和待发送消息按 conversationId 隔离。 */
interface ConversationRuntime {
  blocks: LiveBlock[]
  activeRun: StartRunResponse | null
  isStartingRun: boolean
  approval: { runId: string; approvalId: string } | null
  pendingUserMessage: ConversationHistory['messages'][number] | null
  queuedMessages: QueuedUserMessage[]
  sequences: Set<string>
  unsubscribe: (() => void) | null
}

/** 将运行中尚未出现在持久化历史里的用户消息附加到渲染历史。 */
function mergePendingUserMessage(history: ConversationHistory, pendingUserMessage: ConversationRuntime['pendingUserMessage']): ConversationHistory {
  if (!pendingUserMessage) return history

  const persisted = history.messages.some(message =>
    message.role.toLowerCase() === 'user' && message.content === pendingUserMessage.content)
  return persisted ? history : { ...history, messages: [...history.messages, pendingUserMessage] }
}

/** 创建空的会话运行时，用于初始化或未启动 Run 的会话。 */
const emptyRuntime = (): ConversationRuntime => ({
  blocks: [],
  activeRun: null,
  isStartingRun: false,
  approval: null,
  pendingUserMessage: null,
  queuedMessages: [],
  sequences: new Set<string>(),
  unsubscribe: null,
})

/** NarutoCode 桌面工作台的根视图，协调工作区、会话与运行流。 */
export function App() {
  const [backend, setBackend] = useState<BackendState>({ connected: false, error: null })
  const [workspaces, setWorkspaces] = useState<WorkspaceSummary[]>([])
  const [selectedWorkspace, setSelectedWorkspace] = useState<WorkspaceSummary | null>(null)
  const [conversationsByWorkspace, setConversationsByWorkspace] = useState<Record<string, ConversationSummary[]>>({})
  const [selectedConversation, setSelectedConversation] = useState<ConversationSummary | null>(null)
  const [history, setHistory] = useState<ConversationHistory | null>(null)
  const [conversationLoadError, setConversationLoadError] = useState<string | null>(null)
  const [settings, setSettings] = useState<LlmSettings | null>(null)
  const [query, setQuery] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  // 按会话隔离的运行时状态，SSE 回调始终读写 ref，切换会话时同步到 state
  const runtimesRef = useRef<Map<string, ConversationRuntime>>(new Map())
  // 当前会话 ID 由 SSE 回调读取，避免闭包持有已切换的会话。
  const selectedConversationIdRef = useRef<string | null>(null)
  const [currentRuntime, setCurrentRuntime] = useState<ConversationRuntime>(emptyRuntime)
  const messageEnd = useRef<HTMLDivElement | null>(null)

  /** 加载并缓存指定项目的会话，用于左侧始终展开的项目树。 */
  const loadWorkspaceConversations = async (workspaceId: string) => {
    const value = await window.narutoCode.listConversations(workspaceId)
    setConversationsByWorkspace(current => ({ ...current, [workspaceId]: value }))
    return value
  }

  /** 刷新项目，并并行预加载所有项目的会话以保持导航树展开。 */
  const refreshWorkspaces = async () => {
    const value = await window.narutoCode.listWorkspaces()
    setWorkspaces(value)
    const entries = await Promise.all(value.map(async workspace =>
      [workspace.id, await window.narutoCode.listConversations(workspace.id)] as const))
    setConversationsByWorkspace(Object.fromEntries(entries))
  }
  const refreshConnection = async () => {
    const state = await window.narutoCode.getBackendState()
    setBackend(state)
    if (state.connected) await Promise.all([refreshWorkspaces(), window.narutoCode.getLlmSettings().then(setSettings)])
  }

  useEffect(() => {
    void refreshConnection()
    // 组件卸载时断开所有会话的 SSE 订阅
    return () => {
      for (const rt of runtimesRef.current.values()) rt.unsubscribe?.()
      runtimesRef.current.clear()
    }
  }, [])
  useEffect(() => {
    messageEnd.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [history, currentRuntime.blocks, currentRuntime.approval])

  const selectWorkspace = async (workspace: WorkspaceSummary) => {
    if (selectedWorkspace?.id === workspace.id) return

    setSelectedWorkspace(workspace)
    selectedConversationIdRef.current = null
    setSelectedConversation(null)
    setHistory(null)
    setConversationLoadError(null)
    setCurrentRuntime(emptyRuntime())
    await loadWorkspaceConversations(workspace.id)
  }
  const selectConversation = async (conversation: ConversationSummary) => {
    selectedConversationIdRef.current = conversation.id
    setSelectedConversation(conversation)
    setHistory(null)
    setConversationLoadError(null)
    // 恢复该会话缓存的运行状态（可能正在执行，也可能为空）
    setCurrentRuntime(runtimesRef.current.get(conversation.id) ?? emptyRuntime())

    try {
      const value = normalizeConversationHistory(await window.narutoCode.loadConversation(conversation.id))
      const runtime = runtimesRef.current.get(conversation.id)
      const mergedHistory = mergePendingUserMessage(value, runtime?.pendingUserMessage ?? null)
      if (selectedConversationIdRef.current === conversation.id) setHistory(mergedHistory)
    } catch (error) {
      if (selectedConversationIdRef.current === conversation.id) {
        setConversationLoadError(error instanceof Error ? error.message : '会话历史加载失败。')
      }
    }
  }
  const addWorkspace = async () => {
    const created = await window.narutoCode.addWorkspace()
    if (!created) return
    const existing = (await window.narutoCode.listWorkspaces()).find(item => item.id === created.workspaceId)
    const workspace = existing ?? {
      id: created.workspaceId,
      name: workspaceName(created.workDirectory),
      workDirectory: created.workDirectory,
      lastUpdatedAt: new Date().toISOString(),
      conversationCount: 1,
      directoryExists: true,
    }
    setWorkspaces(current => [workspace, ...current.filter(item => item.id !== workspace.id)])
    await selectWorkspace(workspace)
    await selectConversation(created.conversation)
  }
  const createConversation = async (workspace = selectedWorkspace) => {
    if (!workspace) return
    setSelectedWorkspace(workspace)
    const conversation = await window.narutoCode.createConversation(workspace.id)
    await loadWorkspaceConversations(workspace.id)
    await selectConversation(conversation)
  }

  /** 从始终展开的项目树切换到目标会话。 */
  const selectWorkspaceConversation = async (workspace: WorkspaceSummary, conversation: ConversationSummary) => {
    setSelectedWorkspace(workspace)
    await selectConversation(conversation)
  }
  /** 更新指定会话的运行时，并在它是当前会话时同步到 state 驱动渲染。 */
  const updateRuntime = (conversationId: string, updater: (runtime: ConversationRuntime) => ConversationRuntime) => {
    const current = runtimesRef.current.get(conversationId) ?? emptyRuntime()
    const next = updater(current)
    runtimesRef.current.set(conversationId, next)
    if (selectedConversationIdRef.current === conversationId) setCurrentRuntime(next)
  }
  /** 合并流式内容，兼容上游发送累计快照和纯增量两种协议。 */
  const appendOrMerge = (conversationId: string, kind: LiveBlock['kind'], content: string) => {
    updateRuntime(conversationId, runtime => {
      const last = runtime.blocks.at(-1)
      if (last?.kind === kind && (kind === 'assistant' || kind === 'thinking')) {
        // 新内容包含已渲染文本时是累计快照，应替换而不是重复拼接。
        if (content.startsWith(last.content)) {
          return { ...runtime, blocks: [...runtime.blocks.slice(0, -1), { ...last, content }] }
        }
        // 已完整渲染的重复事件或过期后缀不应再次追加。
        if (last.content.endsWith(content)) return runtime
        return { ...runtime, blocks: [...runtime.blocks.slice(0, -1), { ...last, content: last.content + content }] }
      }

      return {
        ...runtime,
        blocks: [...runtime.blocks, { id: `${kind}-${Date.now()}-${runtime.blocks.length}`, kind, content, status: kind === 'tool' ? 'running' : undefined }],
      }
    })
  }
  const completeLatestTool = (conversationId: string, status: 'completed' | 'failed', content: string | null) => {
    updateRuntime(conversationId, runtime => {
      const index = runtime.blocks.map(block => block.kind).lastIndexOf('tool')
      if (index < 0) {
        return content
          ? { ...runtime, blocks: [...runtime.blocks, { id: `tool-${Date.now()}`, kind: 'tool', content, status }] }
          : runtime
      }

      const tool = runtime.blocks[index]
      const updated = { ...tool, status, content: content || tool.content }
      return { ...runtime, blocks: [...runtime.blocks.slice(0, index), updated, ...runtime.blocks.slice(index + 1)] }
    })
  }
  const completeRunningActivities = (conversationId: string) => {
    updateRuntime(conversationId, runtime => ({
      ...runtime,
      blocks: runtime.blocks.map(block =>
        block.kind === 'tool' || block.kind === 'thinking'
          ? { ...block, status: block.status === 'failed' ? 'failed' : 'completed' }
          : block),
    }))
  }
  /** 为指定会话启动消息对应的 Run；队列出队时可跳过重复占位。 */
  const startRunForMessage = async (conversationId: string, message: QueuedUserMessage, isReserved = false) => {
    if (!isReserved) {
      let reserved = false
      updateRuntime(conversationId, runtime => {
        if (runtime.activeRun || runtime.isStartingRun) return runtime
        reserved = true
        return { ...runtime, isStartingRun: true }
      })
      if (!reserved) return
    }

    const pendingUserMessage: ConversationHistory['messages'][number] = {
      role: 'user',
      messageType: 'Content',
      content: message.content,
      approvalContent: '',
      createdAt: message.createdAt,
      attachments: message.attachments,
    }
    // 先写入 runtime，避免会话历史异步加载完成时覆盖刚提交的乐观消息。
    updateRuntime(conversationId, runtime => ({ ...runtime, pendingUserMessage }))
    if (selectedConversationIdRef.current === conversationId) {
      setHistory(current => ({
        id: current?.id ?? conversationId,
        tokenCount: current?.tokenCount ?? 0,
        messages: [...(current?.messages ?? []), pendingUserMessage],
      }))
    }

    try {
      const run = await window.narutoCode.startRun({
        conversationId,
        content: message.content,
        attachments: message.attachments,
      })
      updateRuntime(conversationId, runtime => ({
        ...runtime,
        activeRun: run,
        isStartingRun: false,
        pendingUserMessage,
      }))
      const unsubscribe = window.narutoCode.onRunEvent(run.runId, applyEvent(conversationId))
      if (runtimesRef.current.get(conversationId)?.activeRun?.runId === run.runId) {
        updateRuntime(conversationId, runtime => ({ ...runtime, unsubscribe }))
      } else {
        unsubscribe()
      }
    } catch (error) {
      updateRuntime(conversationId, runtime => ({ ...runtime, isStartingRun: false }))
      appendOrMerge(conversationId, 'error', error instanceof Error ? error.message : '消息发送失败。')
    }
  }
  /** 从会话队列取出下一条消息，并为其保留唯一的启动槽位。 */
  const startNextQueuedRun = (conversationId: string) => {
    let nextMessage: QueuedUserMessage | null = null
    updateRuntime(conversationId, runtime => {
      if (runtime.activeRun || runtime.isStartingRun || runtime.queuedMessages.length === 0) return runtime
      nextMessage = runtime.queuedMessages[0]
      return { ...runtime, isStartingRun: true, queuedMessages: runtime.queuedMessages.slice(1) }
    })
    if (nextMessage) void startRunForMessage(conversationId, nextMessage, true)
  }
  const applyEvent = (conversationId: string) => (event: RunEvent) => {
    const runtime = runtimesRef.current.get(conversationId)
    if (!runtime) return

    const key = `${event.runId}:${event.sequence}`
    if (runtime.sequences.has(key)) return
    updateRuntime(conversationId, current => ({ ...current, sequences: new Set(current.sequences).add(key) }))

    if ((event.eventType === 'message.delta' || event.eventType === 'plan.delta') && event.content) {
      completeRunningActivities(conversationId)
      appendOrMerge(conversationId, 'assistant', event.content)
    } else if (event.eventType === 'thinking.delta' && event.content) {
      appendOrMerge(conversationId, 'thinking', event.content)
    } else if (event.eventType === 'tool.started' && event.content) {
      completeRunningActivities(conversationId)
      appendOrMerge(conversationId, 'tool', event.content)
    } else if (event.eventType === 'tool.completed') {
      completeLatestTool(conversationId, 'completed', event.content)
    } else if (event.eventType === 'tool.failed') {
      completeLatestTool(conversationId, 'failed', event.content)
    } else if (event.eventType === 'approval.required' && event.approvalId) {
      const approvalId = event.approvalId
      updateRuntime(conversationId, current => ({ ...current, approval: { runId: event.runId, approvalId } }))
    } else if (event.eventType === 'run.failed') {
      appendOrMerge(conversationId, 'error', event.content ?? '运行执行失败。')
    }

    if (!['run.completed', 'run.failed', 'run.cancelled'].includes(event.eventType)) return

    completeRunningActivities(conversationId)
    updateRuntime(conversationId, current => {
      current.unsubscribe?.()
      return { ...current, activeRun: null, approval: null, unsubscribe: null }
    })

    void window.narutoCode.loadConversation(conversationId)
      .then(normalizeConversationHistory)
      .then(value => {
        const activeRuntime = runtimesRef.current.get(conversationId)
        const pendingUserMessage = activeRuntime?.pendingUserMessage ?? null
        const persistedUserMessage = value.messages.some(message =>
          message.role.toLowerCase() === 'user' && message.content === pendingUserMessage?.content)
        const mergedHistory = mergePendingUserMessage(value, pendingUserMessage)
        if (selectedConversationIdRef.current === conversationId) setHistory(mergedHistory)
        updateRuntime(conversationId, current => ({
          ...current,
          blocks: [],
          pendingUserMessage: persistedUserMessage ? null : current.pendingUserMessage,
        }))
      })
      .catch(error => {
        if (selectedConversationIdRef.current === conversationId) {
          setConversationLoadError(error instanceof Error ? error.message : '会话历史加载失败。')
        }
      })
      .finally(() => startNextQueuedRun(conversationId))
  }
  /** 将系统剪贴板中的图片保存到当前工作区，并追加为待发送附件。 */
  const pasteClipboardImage = async (): Promise<boolean> => {
    if (!selectedWorkspace) return false

    const attachment = await window.narutoCode.pasteClipboardImage(selectedWorkspace.workDirectory)
    if (!attachment) return false
    setAttachments(current => current.some(item => item.path === attachment.path) ? current : [...current, attachment])
    return true
  }
  /** 立即发送空闲会话消息，或将运行中会话消息追加到 FIFO 队列。 */
  const send = (draft: string) => {
    if (!selectedConversation || (!draft.trim() && attachments.length === 0)) return

    const conversationId = selectedConversation.id
    const message: QueuedUserMessage = {
      content: draft.trim() || '[图片]',
      attachments,
      createdAt: new Date().toISOString(),
    }
    setAttachments([])

    const runtime = runtimesRef.current.get(conversationId)
    if (runtime?.activeRun || runtime?.isStartingRun) {
      updateRuntime(conversationId, current => ({ ...current, queuedMessages: [...current.queuedMessages, message] }))
      return
    }

    void startRunForMessage(conversationId, message)
  }
  /** 从当前会话的待发送 FIFO 队列中移除指定消息，不影响正在执行的 Run。 */
  const cancelQueuedMessage = (index: number) => {
    if (!selectedConversation) return

    updateRuntime(selectedConversation.id, runtime => ({
      ...runtime,
      queuedMessages: runtime.queuedMessages.filter((_, queuedIndex) => queuedIndex !== index),
    }))
  }
  /** 取消 Run；后端已回收 Run 时，同步清理本地运行状态。 */
  const cancelRun = async () => {
    if (!selectedConversation || !currentRuntime.activeRun) return

    const conversationId = selectedConversation.id
    const runId = currentRuntime.activeRun.runId
    try {
      await window.narutoCode.cancelRun(runId)
    } catch (error) {
      if (!(error instanceof Error && 'status' in error && error.status === 404)) throw error
      updateRuntime(conversationId, runtime => ({ ...runtime, activeRun: null, approval: null, unsubscribe: null }))
      void window.narutoCode.loadConversation(conversationId)
        .then(normalizeConversationHistory)
        .then(value => {
          if (selectedConversationIdRef.current === conversationId) setHistory(value)
        })
        .finally(() => startNextQueuedRun(conversationId))
    }
  }

  const resolveApproval = async (approved: boolean) => {
    if (!currentRuntime.approval || !selectedConversation) return

    const conversationId = selectedConversation.id
    const { runId, approvalId } = currentRuntime.approval
    await window.narutoCode.resolveApproval(runId, approvalId, approved)
    updateRuntime(conversationId, runtime => ({ ...runtime, approval: null }))
  }
  const filtered = useMemo(() => workspaces.filter(workspace => `${workspace.name} ${workspace.workDirectory}`.toLowerCase().includes(query.toLowerCase())), [query, workspaces])

  if (!backend.connected) return <div className="desktop-shell"><aside className="icon-rail"><span className="rail-mark"><Sparkle size={15} weight="fill" /></span></aside><aside className="project-sidebar"><div className="project-brand">NarutoCode</div></aside><StartupError state={backend} retry={async () => { setBackend(await window.narutoCode.restartBackend()); await refreshConnection() }} /></div>

  return <div className="desktop-shell">
    <aside className="icon-rail" aria-label="主导航">
      <span className="rail-mark"><Sparkle size={15} weight="fill" /></span>
      <button className="rail-button active" type="button" aria-label="项目"><FolderOpen size={17} weight="fill" /></button>
      <button className="rail-button" type="button" aria-label="搜索"><MagnifyingGlass size={17} /></button>
      <button className="rail-button" type="button" aria-label="运行"><Play size={17} weight="fill" /></button>
      <button className="rail-button" type="button" aria-label="代码变更"><Code size={17} /></button>
      <span className="rail-spacer" />
      <button className="rail-button" type="button" aria-label="插件"><Plug size={17} /></button>
      <button className="rail-button" type="button" aria-label="自动化"><CalendarDots size={17} /></button>
      <button className="rail-button" type="button" aria-label="设置"><Gear size={17} /></button>
    </aside>
    <aside className="project-sidebar">
      <header className="project-sidebar-head"><div className="project-brand"><span>◇</span>NarutoCode</div><button type="button" aria-label="添加项目" title="添加项目" onClick={() => void addWorkspace()}><Plus size={16} /></button></header>
      <label className="project-search"><MagnifyingGlass size={14} /><input aria-label="搜索项目" placeholder="搜索项目与会话" value={query} onChange={event => setQuery(event.target.value)} /><kbd>⌘K</kbd></label>
      <div className="project-list-head"><span>项目</span><b>{workspaces.length}</b><button type="button" aria-label="打开日志目录" title="打开日志目录" onClick={() => void window.narutoCode.openLogsDirectory()}><DotsThree size={15} weight="bold" /></button></div>
      <nav className="workspace-list" aria-label="工作区列表">
        {filtered.map(workspace => {
          const workspaceConversations = conversationsByWorkspace[workspace.id] ?? []
          return <section className="workspace" key={workspace.id}>
            <div className={`workspace-row ${selectedWorkspace?.id === workspace.id ? 'selected' : ''}`} title={workspace.workDirectory}>
              <button className="workspace-name" type="button" aria-label={`展开 ${workspace.name}`} onClick={() => void selectWorkspace(workspace)}><FolderOpen size={14} weight="fill" />{workspace.name}<span className="workspace-count">{workspace.conversationCount}</span></button>
              <div className="workspace-actions"><button type="button" aria-label={`在 ${workspace.name} 中新建会话`} title="新建会话" onClick={() => void createConversation(workspace)}><Plus size={13} /></button><button type="button" aria-label={`打开 ${workspace.name}`} title="在 Finder 中打开" onClick={() => void window.narutoCode.openWorkspaceFolder(workspace.workDirectory)}><DotsThree size={14} weight="bold" /></button></div>
            </div>
            <div className="conversation-list">{!workspace.directoryExists && <div className="missing">目录当前不可用</div>}{workspaceConversations.map(conversation => <button key={conversation.id} type="button" className={`conversation ${selectedConversation?.id === conversation.id ? 'active' : ''}`} onClick={() => void selectWorkspaceConversation(workspace, conversation)}><span className="conversation-title">{selectedConversation?.id === conversation.id && <Sparkle size={10} weight="fill" />}{conversation.title}</span><span className="conversation-preview">{conversation.updatedAt ? new Date(conversation.updatedAt).toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' }) : ''}</span></button>)}{workspaceConversations.length > 5 && <div className="more-conversations">› 展开更多（{workspaceConversations.length - 5}）</div>}</div>
          </section>
        })}
      </nav>
      <footer className="workspace-path"><span className="workspace-led" />{selectedWorkspace?.workDirectory ?? '选择项目开始协作'}</footer>
    </aside>
    <main className="main">
      <header className="header"><div className="title-area"><span className="conversation-kicker">{selectedWorkspace ? `${selectedWorkspace.name} / ACTIVE SESSION` : 'NARUTOCODE / WORKBENCH'}</span><h1>{selectedConversation?.title ?? '开始新的工作'}</h1>{selectedWorkspace && <span className="header-path">{selectedWorkspace.workDirectory}</span>}</div>
        {settings && <div className="header-controls"><label>模型<select aria-label="模型提供方" value={settings.currentProvider} onChange={async event => { const provider = event.target.value; await window.narutoCode.switchProvider(provider); setSettings(current => current ? { ...current, currentProvider: provider } : current) }}>{settings.providers.map(item => <option key={item}>{item}</option>)}</select></label><label>推理<select aria-label="推理强度" value={settings.currentEffort} onChange={async event => { const effort = event.target.value; await window.narutoCode.switchEffort(effort); setSettings(current => current ? { ...current, currentEffort: effort } : current) }}>{settings.efforts.map(item => <option key={item}>{item}</option>)}</select></label><button className="new-chat" type="button" aria-label="新建对话" onClick={() => void createConversation()} disabled={!selectedWorkspace}><Pen size={14} />新建对话</button></div>}
      </header>
      <section className="message-area"><div className="message-stack">{!selectedConversation ? <div className="empty-state"><div className="empty-orb"><Sparkle size={25} weight="fill" /></div><h2>把代码任务放进一个项目</h2><p>从左侧添加或选择项目，然后创建会话开始协作。</p></div> : <>
        {conversationLoadError && <div className="conversation-load-error" role="alert"><strong>无法加载此会话</strong><span>{conversationLoadError}</span></div>}
        {history?.messages.map((message, index) => {
          const key = `${message.createdAt}-${index}`
          const isUser = message.role.toLowerCase() === 'user'
          const messageType = message.messageType.toLowerCase()
          if (['temporary', 'usage', 'toolapprovalresponse', 'remainingtask'].includes(messageType) || !message.content.trim()) return null
          if (isUser) return <article key={key} className="message user"><div className="message-avatar">你</div><div className="message-body"><div className="message-meta">你</div><div className="message-content">{message.content}</div></div></article>
          if (messageType === 'thinking') return <ActivityBlock key={key} block={{ id: key, kind: 'thinking', content: message.content, status: 'completed' }} />
          if (messageType === 'toolcall' || messageType === 'toolapprovalrequest') return <ActivityBlock key={key} block={{ id: key, kind: 'tool', content: message.content, status: 'completed' }} />
          if (messageType === 'error') return <ActivityBlock key={key} block={{ id: key, kind: 'error', content: message.content, status: 'failed' }} />
          return <article key={key} className="message assistant"><div className="message-avatar"><Sparkle size={15} weight="fill" /></div><div className="message-body"><div className="message-meta">NarutoCode</div><MarkdownContent content={message.content} /></div></article>
        })}
        {currentRuntime.blocks.map(block => block.kind === 'assistant' ? <article key={block.id} className="message assistant"><div className="message-avatar"><Sparkle size={15} weight="fill" /></div><div className="message-body"><div className="message-meta">NarutoCode <span className="live-indicator">正在回复</span></div><MarkdownContent content={block.content} /></div></article> : <ActivityBlock block={block} key={block.id} />)}
        {currentRuntime.queuedMessages.length > 0 && <section className="queued-messages" aria-label="排队消息"><strong>排队消息（{currentRuntime.queuedMessages.length}）</strong>{currentRuntime.queuedMessages.map((message, index) => <div className="queued-message" key={message.createdAt}><span>{index + 1}</span><p>{message.content}</p><button className="queued-message-cancel" type="button" aria-label={`取消排队消息 ${index + 1}`} onClick={() => cancelQueuedMessage(index)}>取消</button></div>)}</section>}
        {(currentRuntime.activeRun || currentRuntime.isStartingRun) && currentRuntime.blocks.length === 0 && <div className="waiting-run"><span /><span /><span />正在启动任务</div>}
        <div ref={messageEnd} /></>}</div></section>
      <Composer conversationId={selectedConversation?.id ?? null} disabled={!selectedConversation} attachments={attachments} isRunning={!!currentRuntime.activeRun || currentRuntime.isStartingRun} queuedMessageCount={currentRuntime.queuedMessages.length} approvalPending={!!currentRuntime.approval} onSend={send} onAddImages={async () => setAttachments(await window.narutoCode.selectImages())} onPasteImage={pasteClipboardImage} onCancel={() => void cancelRun()} onRemoveAttachment={path => setAttachments(current => current.filter(item => item.path !== path))} onResolveApproval={approved => void resolveApproval(approved)} />
    </main>
    <RunContextPanel workspace={selectedWorkspace} conversation={selectedConversation} blocks={currentRuntime.blocks} activeRun={currentRuntime.activeRun} />
  </div>
}
