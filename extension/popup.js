const domain = document.querySelector('#domain');
const toggle = document.querySelector('#toggle');
const status = document.querySelector('#status');
const empty = document.querySelector('#empty');
const list = document.querySelector('#sites');
let config;
let site;
let tabId;

function render() {
  const rows = [...config.sites].sort().map(name => {
    const row = document.createElement('li');
    row.textContent = name;
    return row;
  });
  list.replaceChildren(...rows);
  list.hidden = rows.length === 0;
  empty.hidden = rows.length > 0;
  domain.hidden = !site;
  if (site) domain.textContent = site;
  toggle.hidden = !site || !config.endpoint;
  if (!toggle.hidden) toggle.textContent = config.sites.includes(site) ? 'Убрать из списка' : 'Добавить в список';
  status.textContent = config.endpoint ? '' : 'Сначала выбери локальный прокси.';
}

toggle.addEventListener('click', async () => {
  toggle.disabled = true;
  try {
    config = await updateConfig(current => {
      if (!current.endpoint) throw new Error('Сначала выбери локальный прокси.');
      const sites = current.sites.includes(site) ? current.sites.filter(name => name !== site) : [...current.sites, site];
      return { ...current, sites };
    });
    render();
    if (tabId !== undefined) await chrome.tabs.reload(tabId);
  } catch (error) { status.textContent = error.message; }
  finally { toggle.disabled = false; }
});

document.querySelector('#settings').addEventListener('click', () => chrome.runtime.openOptionsPage());

async function init() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const url = tab?.url ? new URL(tab.url) : null;
  if (url && ['http:', 'https:'].includes(url.protocol)) {
    site = siteKey(url.hostname);
    tabId = tab.id;
  }
  config = await loadConfig();
  render();
}

init().catch(error => { status.textContent = error.message; });
