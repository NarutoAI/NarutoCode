import { CaretDownIcon as CaretDown } from '@phosphor-icons/react/dist/csr/CaretDown'
import { CheckCircleIcon as CheckCircle } from '@phosphor-icons/react/dist/csr/CheckCircle'
import { CircleNotchIcon as CircleNotch } from '@phosphor-icons/react/dist/csr/CircleNotch'
import { TerminalWindowIcon as TerminalWindow } from '@phosphor-icons/react/dist/csr/TerminalWindow'
import { WarningCircleIcon as WarningCircle } from '@phosphor-icons/react/dist/csr/WarningCircle'
import { useState } from 'react'

export type LiveBlock = {
  id: string
  kind: 'assistant' | 'thinking' | 'tool' | 'error'
  content: string
  status?: 'running' | 'completed' | 'failed'
}

function labelForTool(content: string) {
  const firstLine = content.split('\n')[0]?.trim()
  return firstLine || '正在调用工具'
}

/** 显示运行过程中的思考、工具和异常活动。 */
export function ActivityBlock({ block }: { block: LiveBlock }) {
  const [expanded, setExpanded] = useState(block.kind === 'error')

  if (block.kind === 'thinking') {
    const completed = block.status === 'completed'
    return (
      <section className={`activity-block thinking-block ${completed ? 'completed' : ''}`}>
        <button className="activity-summary" type="button" onClick={() => setExpanded(value => !value)} aria-expanded={expanded}>
          <span className="activity-icon">{completed ? <CheckCircle size={16} weight="fill" /> : <CircleNotch size={16} weight="bold" />}</span>
          <span>思考过程</span>
          <span className="activity-state">{completed ? '已完成' : '正在组织回答'}</span>
          <CaretDown className={expanded ? 'chevron expanded' : 'chevron'} size={15} />
        </button>
        {expanded && <pre className="activity-detail">{block.content}</pre>}
      </section>
    )
  }

  if (block.kind === 'tool') {
    const completed = block.status === 'completed'
    const failed = block.status === 'failed'
    return (
      <section className={`activity-block tool-block ${failed ? 'failed' : ''}`}>
        <button className="activity-summary" type="button" onClick={() => setExpanded(value => !value)} aria-expanded={expanded}>
          <span className="activity-icon">{failed ? <WarningCircle size={16} weight="bold" /> : completed ? <CheckCircle size={16} weight="fill" /> : <TerminalWindow size={16} weight="bold" />}</span>
          <span className="tool-name">{labelForTool(block.content)}</span>
          <span className="activity-state">{failed ? '执行失败' : completed ? '已完成' : '执行中'}</span>
          <CaretDown className={expanded ? 'chevron expanded' : 'chevron'} size={15} />
        </button>
        {expanded && <pre className="activity-detail">{block.content}</pre>}
      </section>
    )
  }

  return (
    <section className="activity-block error-block" role="alert">
      <button className="activity-summary" type="button" onClick={() => setExpanded(value => !value)} aria-expanded={expanded}>
        <span className="activity-icon"><WarningCircle size={16} weight="fill" /></span>
        <span>运行遇到问题</span>
        <CaretDown className={expanded ? 'chevron expanded' : 'chevron'} size={15} />
      </button>
      {expanded && <p className="activity-detail error-detail">{block.content}</p>}
    </section>
  )
}
