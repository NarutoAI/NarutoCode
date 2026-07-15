import { randomBytes } from 'node:crypto'
import { spawn, type ChildProcess } from 'node:child_process'
import { join } from 'node:path'
import type { DesktopMainLogger } from './DesktopMainLogger'

export function resolveBackendExecutable(resourcesPath: string, platform = process.platform, arch = process.arch): string {
  if (platform === 'darwin' && arch === 'arm64') {
    return join(resourcesPath, 'backend', 'osx-arm64', 'narutocode-desktop-api')
  }
  if (platform === 'win32' && arch === 'x64') {
    return join(resourcesPath, 'backend', 'win-x64', 'narutocode-desktop-api.exe')
  }
  throw new Error(`Unsupported platform: ${platform}-${arch}`)
}

export interface SidecarConnection {
  baseUrl: string
  token: string
}

/** Starts and owns the Native AOT Desktop API child process. */
export class SidecarManager {
  private child: ChildProcess | undefined
  private stopped = false

  constructor(
    private readonly executable: string,
    private readonly appDataDirectory: string,
    private readonly logger: DesktopMainLogger,
    private readonly startupTimeoutMs = 15_000,
  ) {}

  async start(): Promise<SidecarConnection> {
    if (this.child) {
      throw new Error('Desktop API 已在运行。')
    }

    const token = randomBytes(32).toString('base64url')
    const child = spawn(this.executable, [], {
      env: {
        ...process.env,
        NARUTOCODE_DESKTOP_TOKEN: token,
        NARUTOCODE_DESKTOP_PORT: '0',
        NARUTOCODE_DESKTOP_PARENT_PID: String(process.pid),
        NARUTOCODE_APP_DATA_DIRECTORY: this.appDataDirectory,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    this.child = child
    child.stderr.on('data', data => void this.logger.write('ERROR', `[sidecar] ${data.toString().trim()}`))

    return new Promise<SidecarConnection>((resolve, reject) => {
      let output = ''
      const timeout = setTimeout(() => {
        this.stop()
        reject(new Error('无法启动 NarutoCode 后端：启动超时。'))
      }, this.startupTimeoutMs)
      const fail = (error: Error) => {
        clearTimeout(timeout)
        this.stop()
        reject(error)
      }

      child.once('error', error => fail(new Error(`无法启动 NarutoCode 后端：${error.message}`)))
      child.stdout.on('data', data => {
        output += data.toString()
        const lineEnd = output.indexOf('\n')
        if (lineEnd < 0) return
        const line = output.slice(0, lineEnd).trim()
        try {
          const ready = JSON.parse(line) as { Type?: string; Port?: number }
          if (ready.Type !== 'ready' || !Number.isInteger(ready.Port)) {
            fail(new Error('无法启动 NarutoCode 后端：ready 信号无效。'))
            return
          }
          clearTimeout(timeout)
          resolve({ baseUrl: `http://127.0.0.1:${ready.Port}`, token })
        } catch {
          fail(new Error('无法启动 NarutoCode 后端：ready 信号不是有效 JSON。'))
        }
      })
      child.once('exit', code => {
        if (!this.stopped) fail(new Error(`无法启动 NarutoCode 后端：进程异常退出（${code ?? 'unknown'}）。`))
      })
    })
  }

  stop(): void {
    if (this.stopped) return
    this.stopped = true
    this.child?.kill()
    this.child = undefined
  }
}
