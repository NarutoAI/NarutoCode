import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './app/App'
import { RendererErrorBoundary } from './app/RendererErrorBoundary'
import './app/styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode><RendererErrorBoundary><App /></RendererErrorBoundary></StrictMode>,
)
