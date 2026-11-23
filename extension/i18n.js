function t(key, substitutions) {
  return chrome.i18n.getMessage(key, substitutions);
}

document.documentElement.lang = chrome.i18n.getUILanguage();
for (const element of document.querySelectorAll('[data-i18n]')) {
  element.textContent = t(element.dataset.i18n);
}
