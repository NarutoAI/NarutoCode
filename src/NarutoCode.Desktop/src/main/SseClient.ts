import { createParser } from 'eventsource-parser'

export interface SseEvent {
  id?: string
  event?: string
  data: string
}

/** Reads an authenticated Server-Sent Events response without exposing networking to Renderer. */
export async function readSse(
  response: Response,
  onEvent: (event: SseEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  if (!response.body) {
    throw new Error('SSE 响应不包含可读取的数据流。')
  }

  const parser = createParser({
    onEvent(event) {
      onEvent({ id: event.id, event: event.event, data: event.data })
    },
  })
  const reader = response.body.getReader()
  const decoder = new TextDecoder()

  try {
    while (!signal.aborted) {
      const { done, value } = await reader.read()
      if (done) {
        break
      }

      parser.feed(decoder.decode(value, { stream: true }))
    }
  } finally {
    reader.releaseLock()
  }
}
