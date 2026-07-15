import { readSse } from './SseClient'
import type {
  ConversationHistory,
  ConversationSummary,
  DesktopApiErrorShape,
  LlmSettings,
  RunEvent,
  StartRunRequest,
  StartRunResponse,
  WorkspaceSummary,
} from '../shared/contracts'
import { DesktopApiError } from '../shared/contracts'

/** Authenticated Main-process client for the loopback Desktop API. */
export class DesktopApiClient {
  constructor(
    private readonly baseUrl: string,
    private readonly token: string,
  ) {}

  async health(): Promise<void> {
    const response = await this.request('/health')
    await this.ensureSuccess(response)
  }

  async listWorkspaces(): Promise<WorkspaceSummary[]> {
    return this.get('/api/v1/workspaces/')
  }

  async openWorkspace(workDirectory: string): Promise<{ workspaceId: string; conversation: ConversationSummary; created: boolean }> {
    return this.post('/api/v1/workspaces/', { workDirectory })
  }

  async listConversations(workspaceId: string): Promise<ConversationSummary[]> {
    return this.get(`/api/v1/workspaces/${encodeURIComponent(workspaceId)}/conversations`)
  }

  async createConversation(workspaceId: string): Promise<ConversationSummary> {
    return this.post(`/api/v1/workspaces/${encodeURIComponent(workspaceId)}/conversations`, undefined)
  }

  async loadConversation(conversationId: string): Promise<ConversationHistory> {
    return this.get(`/api/v1/conversations/${encodeURIComponent(conversationId)}`)
  }

  async getLlmSettings(): Promise<LlmSettings> {
    return this.get('/api/v1/settings/llm/')
  }

  async switchProvider(provider: string): Promise<void> {
    await this.put('/api/v1/settings/llm/provider', { provider })
  }

  async switchEffort(effort: string): Promise<void> {
    await this.put('/api/v1/settings/llm/effort', { effort })
  }

  async startRun(request: StartRunRequest): Promise<StartRunResponse> {
    return this.post(`/api/v1/conversations/${encodeURIComponent(request.conversationId)}/runs`, {
      content: request.content,
      attachments: request.attachments,
    })
  }

  async resolveApproval(runId: string, approvalId: string, approved: boolean): Promise<void> {
    await this.post(`/api/v1/runs/${encodeURIComponent(runId)}/approvals/${encodeURIComponent(approvalId)}`, { approved })
  }

  async cancelRun(runId: string): Promise<void> {
    const response = await this.request(`/api/v1/runs/${encodeURIComponent(runId)}/cancel`, { method: 'POST' })
    await this.ensureSuccess(response)
  }

  async subscribeRun(
    eventsUrl: string,
    onEvent: (event: RunEvent) => void,
    signal: AbortSignal,
  ): Promise<void> {
    const response = await this.request(eventsUrl, {
      headers: { Accept: 'text/event-stream' },
      signal,
    })
    await this.ensureSuccess(response)
    await readSse(response, event => onEvent(JSON.parse(event.data) as RunEvent), signal)
  }

  private async get<T>(path: string): Promise<T> {
    const response = await this.request(path)
    await this.ensureSuccess(response)
    return response.json() as Promise<T>
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const response = await this.request(path, {
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    await this.ensureSuccess(response)
    return response.status === 204 ? undefined as T : response.json() as Promise<T>
  }

  private async put(path: string, body: unknown): Promise<void> {
    const response = await this.request(path, { method: 'PUT', body: JSON.stringify(body) })
    await this.ensureSuccess(response)
  }

  private request(path: string, init: RequestInit = {}): Promise<Response> {
    return fetch(new URL(path, this.baseUrl), {
      ...init,
      headers: {
        Authorization: `Bearer ${this.token}`,
        'Content-Type': 'application/json',
        ...init.headers,
      },
    })
  }

  private async ensureSuccess(response: Response): Promise<void> {
    if (response.ok) {
      return
    }

    let error: DesktopApiErrorShape = { code: 'http_error', message: `请求失败：${response.status}` }
    try {
      error = await response.json() as DesktopApiErrorShape
    } catch {
      // The API may fail before its exception middleware is initialized.
    }

    throw new DesktopApiError(error, response.status)
  }
}
