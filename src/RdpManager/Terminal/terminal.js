// SSH terminali: xterm.js <-> uygulama (WebView2 mesajlari).
// Uygulamaya: {t:'ready'} | {t:'in', d:metin} | {t:'size', cols, rows}
// Uygulamadan: duz metin (sunucu ciktisi), terminale yazilir.
const host = window.chrome.webview;
const term = new Terminal({
  cursorBlink: true,
  fontFamily: '"Cascadia Mono", Consolas, monospace',
  fontSize: 14,
  scrollback: 5000,
  theme: { background: '#000000', foreground: '#f5f5f5', cursor: '#ec6b69', selectionBackground: '#5a2b2b' },
});
const fit = new FitAddon.FitAddon();
term.loadAddon(fit);
term.open(document.getElementById('t'));

const send = msg => host.postMessage(JSON.stringify(msg));

// Windows Terminal gibi: secim varsa Ctrl+C kopyalar, yoksa sunucuya ^C gider.
term.attachCustomKeyEventHandler(e => {
  if (e.type === 'keydown' && e.ctrlKey && !e.altKey && e.key.toLowerCase() === 'c' && term.hasSelection()) {
    navigator.clipboard.writeText(term.getSelection());
    term.clearSelection();
    return false;
  }
  return true;
});

term.onData(d => send({ t: 'in', d }));
host.addEventListener('message', e => term.write(e.data));

let lastCols = 0, lastRows = 0;
function resize() {
  fit.fit();
  if (term.cols !== lastCols || term.rows !== lastRows) {
    lastCols = term.cols; lastRows = term.rows;
    send({ t: 'size', cols: term.cols, rows: term.rows });
  }
}
new ResizeObserver(resize).observe(document.getElementById('t'));
window.addEventListener('focus', () => term.focus());

resize();
term.focus();
send({ t: 'ready' });
