import { app, BrowserWindow, dialog, ipcMain, shell } from 'electron'
import { join } from 'node:path'
import started from 'electron-squirrel-startup'
import { DesktopApiClient } from './main/DesktopApiClient'
import { DesktopMainLogger } from './main/DesktopMainLogger'
import { resolveBackendExecutable, SidecarManager } from './main/SidecarManager'
import type { Attachment, BackendState, RunEvent, StartRunRequest, StartRunResponse } from './shared/contracts'

if (started) {
  app.quit()
}

let mainWindow: BrowserWindow | undefined
let appDataDirectory = ''
let logger: DesktopMainLogger | undefined
let sidecar: SidecarManager | undefined
let apiClient: DesktopApiClient | undefined
let backendState: BackendState = { connected: false, error: null }
const runSubscriptions = new Map<string, AbortController>()
const runEventUrls = new Map<string, string>()

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1320,
    height: 860,
    minWidth: 980,
    minHeight: 680,
    titleBarStyle: 'hiddenInset',
    backgroundColor: '#fbfaf7',
    webPreferences: {
      preload: join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  if (MAIN_WINDOW_VITE_DEV_SERVER_URL) {
    void mainWindow.loadURL(MAIN_WINDOW_VITE_DEV_SERVER_URL)
  } else {
    void mainWindow.loadFile(join(__dirname, `../renderer/${MAIN_WINDOW_VITE_NAME}/index.html`))
  }
}

function requireClient(): DesktopApiClient {
  if (!apiClient) {
    throw new Error(backendState.error ?? 'NarutoCode 后端尚未连接。')
  }
  return apiClient
}

async function startSidecar(): Promise<BackendState> {
  try {
    sidecar?.stop()
    const executable = process.env.NARUTOCODE_DESKTOP_API_EXECUTABLE
      ?? resolveBackendExecutable(process.resourcesPath)
    sidecar = new SidecarManager(executable, appDataDirectory, logger!)
    const connection = await sidecar.start()
    apiClient = new DesktopApiClient(connection.baseUrl, connection.token)
    await apiClient.health()
    backendState = { connected: true, error: null }
    await logger?.write('INFO', `Desktop API 已连接：${connection.baseUrl}`)
  } catch (error) {
    apiClient = undefined
    backendState = {
      connected: false,
      error: error instanceof Error ? error.message : '无法启动 NarutoCode 后端。',
    }
    await logger?.write('ERROR', backendState.error ?? '无法启动 NarutoCode 后端。')
  }

  return backendState
}

function registerIpcHandlers(): void {
  ipcMain.handle('desktop:state', () => backendState)
  ipcMain.handle('desktop:restart-sidecar', () => startSidecar())
  ipcMain.handle('desktop:open-logs', () => shell.openPath(join(appDataDirectory, 'Logs')))

  ipcMain.handle('workspace:list', () => requireClient().listWorkspaces())
  ipcMain.handle('workspace:add', async () => {
    const result = await dialog.showOpenDialog(mainWindow!, { properties: ['openDirectory'] })
    if (result.canceled || result.filePaths.length === 0) return null
    const workDirectory = result.filePaths[0]
    const opened = await requireClient().openWorkspace(workDirectory)
    return { ...opened, workDirectory }
  })
  ipcMain.handle('workspace:open-folder', (_event, workDirectory: string) => shell.openPath(workDirectory))
  ipcMain.handle('conversation:list', (_event, workspaceId: string) => requireClient().listConversations(workspaceId))
  ipcMain.handle('conversation:create', (_event, workspaceId: string) => requireClient().createConversation(workspaceId))
  ipcMain.handle('conversation:load', (_event, conversationId: string) => requireClient().loadConversation(conversationId))
  ipcMain.handle('settings:get', () => requireClient().getLlmSettings())
  ipcMain.handle('settings:switch-provider', (_event, provider: string) => requireClient().switchProvider(provider))
  ipcMain.handle('settings:switch-effort', (_event, effort: string) => requireClient().switchEffort(effort))
  ipcMain.handle('attachment:select-images', async (): Promise<Attachment[]> => {
    const result = await dialog.showOpenDialog(mainWindow!, {
      properties: ['openFile', 'multiSelections'],
      filters: [{ name: '图片', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif'] }],
    })
    const mediaTypes: Record<string, string> = {
      '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif',
    }
    return result.canceled ? [] : result.filePaths.map(path => ({
      path,
      mediaType: mediaTypes[path.slice(path.lastIndexOf('.')).toLowerCase()] ?? 'application/octet-stream',
    }))
  })

  ipcMain.handle('run:start', async (_event, request: StartRunRequest): Promise<StartRunResponse> => {
    const response = await requireClient().startRun(request)
    runEventUrls.set(response.runId, response.eventsUrl)
    await logger?.write('INFO', `Run ${response.runId} started; events=${response.eventsUrl}`)
    return response
  })
  ipcMain.handle('run:approve', (_event, runId: string, approvalId: string, approved: boolean) =>
    requireClient().resolveApproval(runId, approvalId, approved))
  ipcMain.handle('run:cancel', (_event, runId: string) => requireClient().cancelRun(runId))
  ipcMain.on('run:subscribe', (event, runId: string) => {
    const existing = runSubscriptions.get(runId)
    const eventsUrl = runEventUrls.get(runId)
    if (existing || !eventsUrl) {
      void logger?.write('ERROR', `Run ${runId} subscription rejected; existing=${!!existing}; hasUrl=${!!eventsUrl}`)
      return
    }
    const controller = new AbortController()
    runSubscriptions.set(runId, controller)
    void logger?.write('INFO', `Run ${runId} SSE subscription opened`)
    void requireClient().subscribeRun(eventsUrl, (runEvent: RunEvent) => {
      void logger?.write('INFO', `Run ${runId} parsed event ${runEvent.sequence} (${runEvent.eventType}); forwarding to Renderer`)
      event.sender.send(`run:event:${runId}`, runEvent)
      if (['run.completed', 'run.failed', 'run.cancelled'].includes(runEvent.eventType)) {
        runSubscriptions.delete(runId)
        runEventUrls.delete(runId)
      }
    }, controller.signal).then(() => {
      void logger?.write('INFO', `Run ${runId} SSE stream ended normally`)
    }).catch(error => {
      if (controller.signal.aborted) return
      event.sender.send(`run:event:${runId}`, {
        runId,
        sequence: Number.MAX_SAFE_INTEGER,
        eventType: 'run.failed',
        timestamp: new Date().toISOString(),
        content: error instanceof Error ? error.message : 'Run 事件流中断。',
        messageType: 'Error',
        approvalContent: null,
        approvalId: null,
      } satisfies RunEvent)
      runSubscriptions.delete(runId)
      runEventUrls.delete(runId)
    })
  })
  ipcMain.on('run:event-received', (_event, runId: string, sequence: number, eventType: string) => {
    void logger?.write('INFO', `Run ${runId} Renderer received event ${sequence} (${eventType})`)
  })
  ipcMain.on('run:unsubscribe', (_event, runId: string) => {
    runSubscriptions.get(runId)?.abort()
    runSubscriptions.delete(runId)
    runEventUrls.delete(runId)
  })
}

app.whenReady().then(async () => {
  appDataDirectory = join(app.getPath('home'), '.narutocode')
  logger = await DesktopMainLogger.create(appDataDirectory)
  await startSidecar()
  registerIpcHandlers()
  createWindow()
})

app.on('before-quit', () => {
  for (const subscription of runSubscriptions.values()) subscription.abort()
  sidecar?.stop()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow()
})
