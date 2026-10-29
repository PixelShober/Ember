import { mount } from 'svelte';
import './app.css';
import App from './App.svelte';
import { setLanguage } from './lib/i18n.svelte';

// Until the host reports its language, follow the browser (WebView2 gets the Windows language).
setLanguage(null);

export default mount(App, { target: document.getElementById('app')! });
