import { contextBridge, ipcRenderer } from 'electron'
import type { NarutoCodeApi, RunEvent, StartRunRequest } from './shared/contracts'

const api: NarutoCodeApi = {
  getBackendState: () => ipcRenderer.invoke('desktop:state'),
  restartBackend: () => ipcRenderer.invoke('desktop:restart-sidecar'),
  openLogsDirectory: () => ipcRenderer.invoke('desktop:open-logs'),
  listWorkspaces: () => ipcRenderer.invoke('workspace:list'),
  addWorkspace: () => ipcRenderer.invoke('workspace:add'),
  openWorkspaceFolder: workDirectory => ipcRenderer.invoke('workspace:open-folder', workDirectory),
  listConversations: workspaceId => ipcRenderer.invoke('conversation:list', workspaceId),
  createConversation: workspaceId => ipcRenderer.invoke('conversation:create', workspaceId),
  loadConversation: conversationId => ipcRenderer.invoke('conversation:load', conversationId),
  getLlmSettings: () => ipcRenderer.invoke('settings:get'),
  switchProvider: provider => ipcRenderer.invoke('settings:switch-provider', provider),
  switchEffort: effort => ipcRenderer.invoke('settings:switch-effort', effort),
  selectImages: () => ipcRenderer.invoke('attachment:select-images'),
  startRun: (request: StartRunRequest) => ipcRenderer.invoke('run:start', request),
  resolveApproval: (runId, approvalId, approved) => ipcRenderer.invoke('run:approve', runId, approvalId, approved),
  cancelRun: runId => ipcRenderer.invoke('run:cancel', runId),
  onRunEvent: (runId, listener) => {
    const channel = `run:event:${runId}`
    const handler = (_event: Electron.IpcRendererEvent, value: RunEvent) => {
      ipcRenderer.send('run:event-received', runId, value.sequence, value.eventType)
      listener(value)
    }
    ipcRenderer.on(channel, handler)
    ipcRenderer.send('run:subscribe', runId)
    return () => {
      ipcRenderer.removeListener(channel, handler)
      ipcRenderer.send('run:unsubscribe', runId)
    }
  },
}

contextBridge.exposeInMainWorld('narutoCode', api)
