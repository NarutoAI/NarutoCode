export interface WorkspaceSummary {
  id: string
  name: string
  workDirectory: string
  lastUpdatedAt: string
  conversationCount: number
  directoryExists: boolean
}

export interface ConversationSummary {
  id: string
  title: string
  createdAt: string
  updatedAt: string
  messageCount: number
  tokenCount: number
  lastUsageTokenCount: number
  lastUserMessagePreview: string
}

export interface Attachment {
  path: string
  mediaType: string
}

export interface ConversationMessage {
  role: string
  messageType: string
  content: string
  approvalContent: string
  createdAt: string
  attachments: Attachment[]
}

export interface ConversationHistory {
  id: string
  tokenCount: number
  messages: ConversationMessage[]
}

export interface LlmSettings {
  currentProvider: string
  currentEffort: string
  providers: string[]
  efforts: string[]
}

export interface StartRunRequest {
  conversationId: string
  content: string
  attachments?: Attachment[]
}

export interface StartRunResponse {
  runId: string
  status: string
  eventsUrl: string
}

export interface RunEvent {
  runId: string
  sequence: number
  eventType: string
  timestamp: string
  content: string | null
  messageType: string | null
  approvalContent: string | null
  approvalId: string | null
}

export interface DesktopApiErrorShape {
  code: string
  message: string
  traceId?: string
  details?: unknown
}

export class DesktopApiError extends Error {
  readonly code: string
  readonly traceId?: string
  readonly details?: unknown

  constructor(error: DesktopApiErrorShape, readonly status: number) {
    super(error.message)
    this.name = 'DesktopApiError'
    this.code = error.code
    this.traceId = error.traceId
    this.details = error.details
  }
}

export interface BackendState {
  connected: boolean
  error: string | null
}

export interface OpenWorkspaceResult {
  workspaceId: string
  workDirectory: string
  conversation: ConversationSummary
  created: boolean
}

export interface NarutoCodeApi {
  getBackendState(): Promise<BackendState>
  restartBackend(): Promise<BackendState>
  openLogsDirectory(): Promise<void>
  listWorkspaces(): Promise<WorkspaceSummary[]>
  addWorkspace(): Promise<OpenWorkspaceResult | null>
  openWorkspaceFolder(workDirectory: string): Promise<void>
  listConversations(workspaceId: string): Promise<ConversationSummary[]>
  createConversation(workspaceId: string): Promise<ConversationSummary>
  loadConversation(conversationId: string): Promise<ConversationHistory>
  getLlmSettings(): Promise<LlmSettings>
  switchProvider(provider: string): Promise<void>
  switchEffort(effort: string): Promise<void>
  selectImages(): Promise<Attachment[]>
  pasteClipboardImage(workDirectory: string): Promise<Attachment | null>
  startRun(request: StartRunRequest): Promise<StartRunResponse>
  resolveApproval(runId: string, approvalId: string, approved: boolean): Promise<void>
  cancelRun(runId: string): Promise<void>
  onRunEvent(runId: string, listener: (event: RunEvent) => void): () => void
}
