const hostName = 'org.siteproxyswitcher.discovery';
const input = document.querySelector('#endpoint');
const status = document.querySelector('#status');
const save = document.querySelector('#save');
const detect = document.querySelector('#detect');
const candidates = document.querySelector('#candidates');
const clients = {
  happ: 'Happ', koala: 'Koala Clash', verge: 'Clash Verge Rev', v2rayn: 'v2rayN',
};

async function saveEndpoint(endpoint) {
  save.disabled = true;
  try {
    await updateConfig(current => ({ ...current, endpoint }));
    input.value = formatEndpoint(endpoint);
    status.textContent = `Сохранено ${input.value}`;
  } catch (error) { status.textContent = error.message; }
  finally { save.disabled = false; }
}

document.querySelector('#manual').addEventListener('submit', event => {
  event.preventDefault();
  try { void saveEndpoint(parseEndpoint(input.value)); }
  catch (error) { status.textContent = error.message; }
});

function showCandidates(found) {
  const rows = [];
  for (const item of found.slice(0, 24)) {
    if (!item || !Object.hasOwn(clients, item.client)) continue;
    let endpoint;
    try { endpoint = parseEndpoint(formatEndpoint(item)); }
    catch { continue; }
    const row = document.createElement('li');
    const label = document.createElement('span');
    label.textContent = `${clients[item.client]} · ${formatEndpoint(endpoint)}`;
    row.append(label, document.createElement('br'));
    if (endpoint.protocol === 'socks5' && item.authRequired === true) {
      const hint = document.createElement('span');
      hint.textContent = 'Chrome не поддерживает пароль для SOCKS5. Выбери HTTP, если он доступен.';
      row.append(hint);
    } else {
      if (endpoint.protocol === 'http' && item.authRequired === true) {
        const hint = document.createElement('p');
        hint.textContent = 'HTTP-прокси запросил авторизацию.';
        row.append(hint);
      }
      const use = document.createElement('button');
      use.textContent = 'Использовать';
      use.addEventListener('click', () => saveEndpoint(endpoint));
      row.append(use);
    }
    rows.push(row);
  }
  candidates.replaceChildren(...rows);
  status.textContent = rows.length
    ? 'Обнаружены локальные порты. Куда клиент направит трафик, определяют его правила.'
    : 'Локальный прокси не найден. Проверь, что клиент подключён, или укажи адрес вручную.';
}

detect.addEventListener('click', async () => {
  detect.disabled = true;
  candidates.replaceChildren();
  let granted = false;
  try {
    granted = await chrome.permissions.request({ permissions: ['nativeMessaging'] });
    if (!granted) {
      status.textContent = 'Разрешение на автоопределение не выдано. Ручная настройка доступна.';
      return;
    }
    status.textContent = 'Ищу локальный прокси…';
    const result = await chrome.runtime.sendNativeMessage(hostName, { command: 'discover' });
    if (result?.ok !== true || !Array.isArray(result.candidates)) throw new Error('Помощник не смог проверить локальные порты.');
    showCandidates(result.candidates);
  } catch (error) {
    status.textContent = /native messaging host/i.test(error.message)
      ? 'Установи помощник из архива через Install-Discovery.cmd или введи адрес вручную.'
      : error.message;
  } finally {
    if (granted) await chrome.permissions.remove({ permissions: ['nativeMessaging'] }).catch(() => {});
    detect.disabled = false;
  }
});

loadConfig().then(({ endpoint }) => { if (endpoint) input.value = formatEndpoint(endpoint); })
  .catch(error => { status.textContent = error.message; });
