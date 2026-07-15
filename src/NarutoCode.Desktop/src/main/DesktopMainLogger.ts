import { appendFile, mkdir } from 'node:fs/promises'
import { join } from 'node:path'

/** Serializes Electron main-process diagnostics into the shared app-data Logs directory. */
export class DesktopMainLogger {
  private pending = Promise.resolve()

  private constructor(private readonly logFile: string) {}

  static async create(appDataDirectory: string): Promise<DesktopMainLogger> {
    const logsDirectory = join(appDataDirectory, 'Logs')
    await mkdir(logsDirectory, { recursive: true })
    const date = new Date().toISOString().slice(0, 10).replaceAll('-', '')
    return new DesktopMainLogger(join(logsDirectory, `desktop-main-${date}.log`))
  }

  write(level: 'INFO' | 'ERROR', message: string): Promise<void> {
    const line = `${new Date().toISOString()} [${level}] ${message}\n`
    this.pending = this.pending.then(() => appendFile(this.logFile, line, 'utf8'))
    return this.pending
  }
}
