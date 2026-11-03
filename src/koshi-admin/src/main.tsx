import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './index.css'

if (localStorage.getItem('koshi-theme') === 'dark') document.documentElement.classList.add('dark')

// After a new deploy, an open tab may reference old chunk files that no longer exist.
// Recover automatically by reloading once (guarded against reload loops).
function recoverFromStaleChunk() {
  const key = 'koshi-chunk-reload'
  const last = Number(sessionStorage.getItem(key) ?? 0)
  if (Date.now() - last > 15_000) {
    sessionStorage.setItem(key, String(Date.now()))
    window.location.reload()
  }
}
window.addEventListener('vite:preloadError', (e) => {
  e.preventDefault()
  recoverFromStaleChunk()
})
window.addEventListener('unhandledrejection', (e) => {
  const msg = String((e.reason as Error)?.message ?? e.reason ?? '')
  if (/dynamically imported module|Importing a module script failed|Failed to fetch/i.test(msg)) recoverFromStaleChunk()
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
