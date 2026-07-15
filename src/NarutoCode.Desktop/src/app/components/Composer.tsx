import { ArrowUpIcon as ArrowUp } from '@phosphor-icons/react/dist/csr/ArrowUp'
import { PaperclipIcon as Paperclip } from '@phosphor-icons/react/dist/csr/Paperclip'
import { ShieldCheckIcon as ShieldCheck } from '@phosphor-icons/react/dist/csr/ShieldCheck'
import { StopIcon as Stop } from '@phosphor-icons/react/dist/csr/Stop'
import { XIcon as X } from '@phosphor-icons/react/dist/csr/X'
import type { Attachment } from '../../shared/contracts'

interface ComposerProps {
  disabled: boolean
  draft: string
  attachments: Attachment[]
  isRunning: boolean
  approvalPending: boolean
  onDraftChange: (value: string) => void
  onSend: () => void
  onAddImages: () => void
  onCancel: () => void
  onRemoveAttachment: (path: string) => void
  onResolveApproval: (approved: boolean) => void
}

/** 提供附件、审批和取消能力的浮动消息编辑器。 */
export function Composer({
  disabled, draft, attachments, isRunning, approvalPending, onDraftChange, onSend, onAddImages, onCancel, onRemoveAttachment, onResolveApproval,
}: ComposerProps) {
  return (
    <section className="composer-wrap">
      {approvalPending && (
        <div className="approval-card">
          <div className="approval-copy"><ShieldCheck size={19} weight="fill" /><div><strong>等待工具授权</strong><span>该操作可能读取或修改当前项目中的文件。</span></div></div>
          <div className="approval-actions"><button className="button quiet" type="button" onClick={() => onResolveApproval(false)}>拒绝</button><button className="button approve" type="button" onClick={() => onResolveApproval(true)}>允许执行</button></div>
        </div>
      )}
      {attachments.length > 0 && <div className="attachment-row">{attachments.map(item => <span className="attachment-chip" key={item.path}>{item.path.split('/').at(-1)}<button type="button" aria-label={`移除 ${item.path.split('/').at(-1)}`} onClick={() => onRemoveAttachment(item.path)}><X size={13} /></button></span>)}</div>}
      <div className={`composer ${disabled ? 'disabled' : ''}`}>
        <textarea aria-label="输入消息" value={draft} disabled={disabled} placeholder={disabled ? '请先选择一个会话' : '询问、描述任务，或粘贴代码…'} onChange={event => onDraftChange(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); onSend() } }} />
        <div className="composer-footer">
          <button className="composer-icon" type="button" aria-label="添加图片附件" title="添加图片" disabled={disabled || isRunning} onClick={onAddImages}><Paperclip size={19} /></button>
          <span className="composer-hint">Enter 发送 · Shift + Enter 换行</span>
          {isRunning ? <button className="send-button stop" type="button" aria-label="取消运行" onClick={onCancel}><Stop size={15} weight="fill" /></button> : <button className="send-button" type="button" aria-label="发送消息" disabled={disabled || !draft.trim()} onClick={onSend}><ArrowUp size={18} weight="bold" /></button>}
        </div>
      </div>
    </section>
  )
}
