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

/** NarutoCode 桌面工作台的根视图，协调工作区、会话与运行流。 */
export function App() {
  const [backend, setBackend] = useState<BackendState>({ connected: false, error: null })
  const [workspaces, setWorkspaces] = useState<WorkspaceSummary[]>([])
  const [selectedWorkspace, setSelectedWorkspace] = useState<WorkspaceSummary | null>(null)
  const [conversations, setConversations] = useState<ConversationSummary[]>([])
  const [selectedConversation, setSelectedConversation] = useState<ConversationSummary | null>(null)
  const [history, setHistory] = useState<ConversationHistory | null>(null)
  const [conversationLoadError, setConversationLoadError] = useState<string | null>(null)
  const [settings, setSettings] = useState<LlmSettings | null>(null)
  const [query, setQuery] = useState('')
  const [draft, setDraft] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [activeRun, setActiveRun] = useState<StartRunResponse | null>(null)
  const [blocks, setBlocks] = useState<LiveBlock[]>([])
  const [approval, setApproval] = useState<{ runId: string; approvalId: string } | null>(null)
  const unsubscribe = useRef<(() => void) | null>(null)
  const sequences = useRef(new Set<string>())
  const messageEnd = useRef<HTMLDivElement | null>(null)

  const refreshWorkspaces = async () => setWorkspaces(await window.narutoCode.listWorkspaces())
  const refreshConnection = async () => {
    const state = await window.narutoCode.getBackendState()
    setBackend(state)
    if (state.connected) await Promise.all([refreshWorkspaces(), window.narutoCode.getLlmSettings().then(setSettings)])
  }

  useEffect(() => {
    void refreshConnection()
    return () => {
      unsubscribe.current?.()
    }
  }, [])
  useEffect(() => {
    messageEnd.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [history, blocks, approval])

  const selectWorkspace = async (workspace: WorkspaceSummary) => {
    setSelectedWorkspace(workspace)
    setSelectedConversation(null)
    setHistory(null)
    setConversationLoadError(null)
    setBlocks([])
    setApproval(null)
    setConversations(await window.narutoCode.listConversations(workspace.id))
  }
  const selectConversation = async (conversation: ConversationSummary) => {
    setSelectedConversation(conversation)
    setHistory(null)
    setConversationLoadError(null)
    setBlocks([])
    setApproval(null)

    try {
      setHistory(normalizeConversationHistory(await window.narutoCode.loadConversation(conversation.id)))
    } catch (error) {
      setConversationLoadError(error instanceof Error ? error.message : '会话历史加载失败。')
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
  const createConversation = async () => {
    if (!selectedWorkspace) return
    const conversation = await window.narutoCode.createConversation(selectedWorkspace.id)
    setConversations(await window.narutoCode.listConversations(selectedWorkspace.id))
    await selectConversation(conversation)
  }
  const appendOrMerge = (kind: LiveBlock['kind'], content: string) => {
    setBlocks(current => {
      const last = current.at(-1)
      if (last?.kind === kind && (kind === 'assistant' || kind === 'thinking')) return [...current.slice(0, -1), { ...last, content: last.content + content }]
      return [...current, { id: `${kind}-${Date.now()}-${current.length}`, kind, content, status: kind === 'tool' ? 'running' : undefined }]
    })
  }
  const completeLatestTool = (status: 'completed' | 'failed', content: string | null) => {
    setBlocks(current => {
      const index = [...current].map(block => block.kind).lastIndexOf('tool')
      if (index < 0) return content ? [...current, { id: `tool-${Date.now()}`, kind: 'tool', content, status }] : current
      const tool = current[index]
      const updated = { ...tool, status, content: content || tool.content }
      return [...current.slice(0, index), updated, ...current.slice(index + 1)]
    })
  }
  const completeRunningActivities = () => {
    setBlocks(current => current.map(block =>
      block.kind === 'tool' || block.kind === 'thinking'
        ? { ...block, status: block.status === 'failed' ? 'failed' : 'completed' }
        : block))
  }
  const applyEvent = (event: RunEvent) => {
    const key = `${event.runId}:${event.sequence}`
    if (sequences.current.has(key)) return
    sequences.current.add(key)
    if ((event.eventType === 'message.delta' || event.eventType === 'plan.delta') && event.content) {
      completeRunningActivities()
      appendOrMerge('assistant', event.content)
    } else if (event.eventType === 'thinking.delta' && event.content) {
      appendOrMerge('thinking', event.content)
    } else if (event.eventType === 'tool.started' && event.content) {
      completeRunningActivities()
      appendOrMerge('tool', event.content)
    } else if (event.eventType === 'tool.completed') {
      completeLatestTool('completed', event.content)
    } else if (event.eventType === 'tool.failed') {
      completeLatestTool('failed', event.content)
    } else if (event.eventType === 'approval.required' && event.approvalId) {
      setApproval({ runId: event.runId, approvalId: event.approvalId })
    } else if (event.eventType === 'run.failed') {
      appendOrMerge('error', event.content ?? '运行执行失败。')
    }

    if (['run.completed', 'run.failed', 'run.cancelled'].includes(event.eventType)) {
      completeRunningActivities()
      unsubscribe.current?.()
      unsubscribe.current = null
      setActiveRun(null)
      setApproval(null)
      if (selectedConversation) {
        void window.narutoCode.loadConversation(selectedConversation.id)
          .then(normalizeConversationHistory)
          .then(value => {
            setHistory(value)
            setBlocks([])
          })
          .catch(error => setConversationLoadError(error instanceof Error ? error.message : '会话历史加载失败。'))
      }
    }
  }
  const send = async () => {
    if (!selectedConversation || !draft.trim() || activeRun) return
    const content = draft.trim()
    const sentAttachments = attachments
    const run = await window.narutoCode.startRun({ conversationId: selectedConversation.id, content, attachments: sentAttachments })
    setHistory(current => ({
      id: current?.id ?? selectedConversation.id,
      tokenCount: current?.tokenCount ?? 0,
      messages: [...(current?.messages ?? []), {
        role: 'user',
        messageType: 'Content',
        content,
        approvalContent: '',
        createdAt: new Date().toISOString(),
        attachments: sentAttachments,
      }],
    }))
    setDraft('')
    setAttachments([])
    setActiveRun(run)
    setBlocks([])
    sequences.current.clear()
    unsubscribe.current = window.narutoCode.onRunEvent(run.runId, applyEvent)
  }
  const resolveApproval = async (approved: boolean) => {
    if (!approval) return
    await window.narutoCode.resolveApproval(approval.runId, approval.approvalId, approved)
    setApproval(null)
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
        {filtered.map(workspace => <section className="workspace" key={workspace.id}>
          <div className={`workspace-row ${selectedWorkspace?.id === workspace.id ? 'selected' : ''}`} title={workspace.workDirectory}>
            <button className="workspace-name" type="button" aria-label={`展开 ${workspace.name}`} onClick={() => void selectWorkspace(workspace)}><FolderOpen size={14} weight="fill" />{workspace.name}<span className="workspace-count">{workspace.conversationCount}</span></button>
            <div className="workspace-actions"><button type="button" aria-label={`在 ${workspace.name} 中新建会话`} title="新建会话" onClick={() => { if (selectedWorkspace?.id !== workspace.id) void selectWorkspace(workspace); else void createConversation() }}><Plus size={13} /></button><button type="button" aria-label={`打开 ${workspace.name}`} title="在 Finder 中打开" onClick={() => void window.narutoCode.openWorkspaceFolder(workspace.workDirectory)}><DotsThree size={14} weight="bold" /></button></div>
          </div>
          {selectedWorkspace?.id === workspace.id && <div className="conversation-list">{!workspace.directoryExists && <div className="missing">目录当前不可用</div>}{conversations.map(conversation => <button key={conversation.id} type="button" className={`conversation ${selectedConversation?.id === conversation.id ? 'active' : ''}`} onClick={() => void selectConversation(conversation)}><span className="conversation-title">{selectedConversation?.id === conversation.id && <Sparkle size={10} weight="fill" />}{conversation.title}</span><span className="conversation-preview">{conversation.updatedAt ? new Date(conversation.updatedAt).toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' }) : ''}</span></button>)}{conversations.length > 5 && <div className="more-conversations">› 展开更多（{conversations.length - 5}）</div>}</div>}
        </section>)}
      </nav>
      <footer className="workspace-path"><span className="workspace-led" />{selectedWorkspace?.workDirectory ?? '选择项目开始协作'}</footer>
    </aside>
    <main className="main">
      <header className="header"><div className="title-area"><span className="conversation-kicker">{selectedWorkspace ? `${selectedWorkspace.name} / ACTIVE SESSION` : 'NARUTOCODE / WORKBENCH'}</span><h1>{selectedConversation?.title ?? '开始新的工作'}</h1>{selectedWorkspace && <span className="header-path">{selectedWorkspace.workDirectory}</span>}</div>
        {settings && <div className="header-controls"><label>模型<select aria-label="模型提供方" value={settings.currentProvider} onChange={async event => { await window.narutoCode.switchProvider(event.target.value); setSettings(await window.narutoCode.getLlmSettings()) }}>{settings.providers.map(item => <option key={item}>{item}</option>)}</select></label><label>推理<select aria-label="推理强度" value={settings.currentEffort} onChange={async event => { await window.narutoCode.switchEffort(event.target.value); setSettings(await window.narutoCode.getLlmSettings()) }}>{settings.efforts.map(item => <option key={item}>{item}</option>)}</select></label><button className="new-chat" type="button" aria-label="新建对话" onClick={() => void createConversation()} disabled={!selectedWorkspace}><Pen size={14} />新建对话</button></div>}
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
        {blocks.map(block => block.kind === 'assistant' ? <article key={block.id} className="message assistant"><div className="message-avatar"><Sparkle size={15} weight="fill" /></div><div className="message-body"><div className="message-meta">NarutoCode <span className="live-indicator">正在回复</span></div><MarkdownContent content={block.content} /></div></article> : <ActivityBlock block={block} key={block.id} />)}
        {activeRun && blocks.length === 0 && <div className="waiting-run"><span /><span /><span />正在启动任务</div>}
        <div ref={messageEnd} /></>}</div></section>
      <Composer disabled={!selectedConversation} draft={draft} attachments={attachments} isRunning={!!activeRun} approvalPending={!!approval} onDraftChange={setDraft} onSend={() => void send()} onAddImages={async () => setAttachments(await window.narutoCode.selectImages())} onCancel={() => activeRun && void window.narutoCode.cancelRun(activeRun.runId)} onRemoveAttachment={path => setAttachments(current => current.filter(item => item.path !== path))} onResolveApproval={approved => void resolveApproval(approved)} />
    </main>
    <RunContextPanel workspace={selectedWorkspace} conversation={selectedConversation} blocks={blocks} activeRun={activeRun} />
  </div>
}
