import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { BroadcastView } from './components/BroadcastView'
import { A11yProvider } from './a11y/A11yContext'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {['/overlay', '/broadcast'].includes(location.pathname) ? <BroadcastView /> : <A11yProvider><App /></A11yProvider>}
  </StrictMode>,
)
