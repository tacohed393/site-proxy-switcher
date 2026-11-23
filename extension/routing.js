const scope = 'regular_only';
const youtube = [
  'youtube.com', 'youtu.be', 'googlevideo.com', 'ytimg.com',
  'youtube-nocookie.com', 'youtubei.googleapis.com', 'youtube.googleapis.com',
];

function parseEndpoint(value) {
  const match = /^(http|socks5):\/\/(localhost|127\.0\.0\.1|\[::1\]):(\d{1,5})$/i.exec(value.trim());
  if (!match) throw new Error(t('invalidEndpoint'));
  const [, protocol, address, portText] = match;
  const port = Number(portText);
  if (port < 1 || port > 65535) throw new Error(t('invalidPort'));
  return { protocol: protocol.toLowerCase(), host: address.toLowerCase() === 'localhost' ? '127.0.0.1' : address, port };
}

function formatEndpoint(endpoint) {
  return `${endpoint.protocol}://${endpoint.host}:${endpoint.port}`;
}

function siteKey(hostname) {
  const name = hostname.toLowerCase().replace(/^www\./, '').replace(/\.$/, '');
  const domain = /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(name);
  if (!domain || /^(?:\d+\.){3}\d+$/.test(name) || name.endsWith('.localhost') || name.endsWith('.local')) return null;
  return youtube.some(item => name === item || name.endsWith(`.${item}`)) ? 'youtube.com' : name;
}

async function loadConfig() {
  const data = await chrome.storage.local.get(['endpoint', 'sites']);
  let endpoint = null;
  try { if (data.endpoint) endpoint = parseEndpoint(formatEndpoint(data.endpoint)); } catch { /* An invalid local setting cannot become a proxy. */ }
  const saved = Array.isArray(data.sites) ? data.sites : [];
  const sites = [...new Set(saved.map(item => typeof item === 'string' && siteKey(item)).filter(Boolean))];
  return { endpoint, sites };
}

function proxyScript(endpoint, sites) {
  const checked = parseEndpoint(formatEndpoint(endpoint));
  const server = `${checked.protocol === 'socks5' ? 'SOCKS5' : 'PROXY'} ${checked.host}:${checked.port}`;
  const domains = [...new Set(sites.flatMap(site => site === 'youtube.com' ? youtube : [site]))];
  return `function FindProxyForURL(url, host) {
    host = host.toLowerCase().replace(/\\.$/, '');
    var domains = ${JSON.stringify(domains)};
    for (var i = 0; i < domains.length; i++) {
      if (host === domains[i] || dnsDomainIs(host, '.' + domains[i])) return ${JSON.stringify(server)};
    }
    return 'DIRECT';
  }`;
}

async function applyRouting({ endpoint, sites }) {
  const current = await chrome.proxy.settings.get({ incognito: false });
  if (!endpoint || !sites.length) {
    if (current.levelOfControl === 'controlled_by_this_extension') await chrome.proxy.settings.clear({ scope });
    return;
  }
  if (!['controllable_by_this_extension', 'controlled_by_this_extension'].includes(current.levelOfControl)) {
    throw new Error(t('proxyControlled'));
  }
  const data = proxyScript(endpoint, sites);
  if (current.levelOfControl !== 'controlled_by_this_extension' || data !== current.value?.pacScript?.data) {
    await chrome.proxy.settings.set({ scope, value: { mode: 'pac_script', pacScript: { data, mandatory: true } } });
  }
}

function updateConfig(update) {
  return navigator.locks.request('site-proxy-config', async () => {
    const current = await loadConfig();
    const next = update(current);
    await applyRouting(next);
    try { await chrome.storage.local.set(next); }
    catch (error) { await applyRouting(current); throw error; }
    return next;
  });
}
