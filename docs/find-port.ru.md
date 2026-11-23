# Как найти порт локального прокси

[English](find-port.md) · [README](../README.ru.md)

## 1. Посмотри настройки VPN-клиента

Подключи клиент и найди в настройках **HTTP-прокси**, **SOCKS5-прокси** или **Mixed port**. На том же компьютере адрес обычно `127.0.0.1`. Порт возьми из своего приложения. Числа ниже приведены для примера.

| Клиент | Где искать |
| --- | --- |
| Koala Clash или Clash Verge Rev | В настройках клиента ищи порт локального прокси или меню настройки портов. В конфиге Mihomo `mixed-port` принимает HTTP и SOCKS5, `port` отвечает за HTTP, `socks-port` за SOCKS5. `external-controller` используется для API клиента. [Порты Mihomo](https://wiki.metacubex.one/en/config/inbound/port/) |
| Happ | В настройках приложения найди порты HTTP и SOCKS. Для работы на одном компьютере доступ из локальной сети можно оставить выключенным. [Релизы Happ для ПК](https://github.com/Happ-proxy/happ-desktop/releases) |
| v2rayN | Посмотри локальный порт в настройках и проверь его тип. Он может принимать оба протокола или один. [Вики v2rayN](https://github.com/2dust/v2rayN/wiki/Description-of-some-ui) |
| Другой клиент | Ищи настройку локального прокси или уточни у провайдера, есть ли она. Клиенту, у которого есть только системный туннель, для работы с расширением понадобится отдельный локальный прокси. |

## 2. Посмотри настройки системы

- **Windows.** Открой **Параметры → Сеть и Интернет → Прокси-сервер**. Если клиент включил системный прокси, адрес и порт будут в ручной настройке. Для поиска по процессу открой **Диспетчер задач → Подробности**, найди PID клиента или его сетевого ядра и выполни в PowerShell `Get-NetTCPConnection -State Listen -OwningProcess 1234`, подставив этот PID. Смотри `LocalPort` у адресов `127.0.0.1` или `::1`. [Системный прокси Windows](https://support.microsoft.com/en-us/windows/experience/connectivity-networking/use-a-proxy-server-in-windows) · [Get-NetTCPConnection](https://learn.microsoft.com/en-us/powershell/module/nettcpip/get-nettcpconnection)
- **macOS.** Открой **Системные настройки → Сеть → твоё подключение → Подробнее → Прокси** или выполни в Терминале `scutil --proxy`. Для включённого HTTP-прокси смотри поля `HTTPProxy` и `HTTPPort`. Чтобы посмотреть порты процесса, возьми его PID в «Мониторинге системы» и подставь в команду `lsof -nP -a -p 1234 -iTCP -sTCP:LISTEN`. [Системный прокси macOS](https://support.apple.com/en-us/guide/mac-help/mchlp2591/mac) · [Справка lsof](https://github.com/lsof-org/lsof/blob/master/docs/manpage.md)

У одного клиента может быть несколько портов. Сверь тип прокси с настройками приложения и проверь подходящий порт.

## 3. Проверь порт и введи его в Chrome

Запусти одну из команд в Терминале macOS со своим номером порта. В командной строке Windows (`cmd.exe`) замени `curl` на `curl.exe`. Тест отправит один запрос к `example.com` через локальный прокси. [Справка curl](https://curl.se/docs/manpage.html)

```sh
curl -q --noproxy "" --proxy http://127.0.0.1:7890 --max-time 10 --head https://example.com/
curl -q --noproxy "" --proxy socks5h://127.0.0.1:10808 --max-time 10 --head https://example.com/
```

Если сайт ответил, впиши в **Настроить прокси** адрес вида `http://127.0.0.1:7890` или `socks5://127.0.0.1:10808` со своим портом. Если прокси слушает только IPv6, используй адрес `[::1]`. Успешный ответ подтверждает работу прокси. Чтобы убедиться, что трафик ушёл в VPN, посмотри маршрут в журнале подключений своего клиента.

- **Отказ в подключении или таймаут.** Убедись, что клиент запущен, и проверь порт и протокол.
- **HTTP 407.** HTTP-прокси просит авторизацию. Chrome может запросить учётные данные. Для SOCKS5 используй доверенный локальный прокси без авторизации SOCKS5 или выбери HTTP-порт. [Прокси-протоколы Chromium](https://chromium.googlesource.com/chromium/src/+/main/net/docs/proxy.md#proxy-server-schemes)
