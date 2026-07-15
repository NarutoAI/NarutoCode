import { describe, expect, it, vi } from 'vitest'
import { readSse } from './SseClient'

/** 验证 SSE 帧被任意网络分块拆分时仍可逐条实时解析。 */
describe('readSse', () => {
  it('parses events split across response chunks', async () => {
    const encoder = new TextEncoder()
    const chunks = [
      'id: 1\nevent: thinking.delta\ndata: {"sequence":1,"eventType":"thinking.delta"}\n',
      '\nid: 2\nevent: message.delta\ndata: {"sequence":2,"eventType":"message.delta"}',
      '\n\nid: 3\nevent: run.completed\ndata: {"sequence":3,"eventType":"run.completed"}\n\n',
    ]
    const response = new Response(new ReadableStream({
      start(controller) {
        for (const chunk of chunks) controller.enqueue(encoder.encode(chunk))
        controller.close()
      },
    }))
    const received = vi.fn()

    await readSse(response, received, new AbortController().signal)

    expect(received.mock.calls.map(([event]) => event.event)).toEqual([
      'thinking.delta',
      'message.delta',
      'run.completed',
    ])
  })
})
