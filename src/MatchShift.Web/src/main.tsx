import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { BroadcastView } from './components/BroadcastView'
import { A11yProvider } from './a11y/A11yContext'
import { broadcastRoute } from './broadcastRoute'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {broadcastRoute(location.pathname, import.meta.env.BASE_URL) ? <BroadcastView /> : <A11yProvider><App /></A11yProvider>}
  </StrictMode>,
)
