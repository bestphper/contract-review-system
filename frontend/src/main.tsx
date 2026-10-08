import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { App as AntApp, ConfigProvider } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { HashRouter } from 'react-router-dom';
import { api, User } from './api';
import App from './App';
import './styles.css';

const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
function Root() { const [user,setUser] = useState<User|null>(null); const [loading,setLoading] = useState(true); useEffect(() => { api.me().then(setUser).catch(() => {}).finally(() => setLoading(false)); const expired=()=>setUser(null); window.addEventListener('auth-expired',expired); return()=>window.removeEventListener('auth-expired',expired); },[]); if (loading) return <div className="page-loading">正在加载...</div>; return <App user={user} onLogin={setUser} onLogout={()=>{api.logout();setUser(null)}}/>; }
createRoot(document.getElementById('root')!).render(<React.StrictMode><ConfigProvider theme={{token:{colorPrimary:'#1769aa',borderRadius:8}}}><AntApp><QueryClientProvider client={client}><HashRouter><Root/></HashRouter></QueryClientProvider></AntApp></ConfigProvider></React.StrictMode>);
