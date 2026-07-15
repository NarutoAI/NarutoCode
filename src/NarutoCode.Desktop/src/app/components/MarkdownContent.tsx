import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

/**
 * 将助手返回的 Markdown 以受控组件渲染。
 * 不启用 raw HTML，避免模型内容插入未经处理的 DOM。
 */
export function MarkdownContent({ content }: { content: string }) {
  return (
    <div className="markdown-content">
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{content}</ReactMarkdown>
    </div>
  )
}
