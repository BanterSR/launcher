'use strict';
// Developer build only (copied into dist\dev, never into the user build).
// Adds the run log, per-server endpoint overrides, test password login,
// client console, font and launch arguments on top of ui/app.js.
(() => {
  const L = window.Launcher;
  const { esc, attr, sw, section, send } = L;

  // ---------- run log (persistent element, survives re-renders) ----------
  const log = document.createElement('div');
  log.className = 'dev-log-lines';
  log.setAttribute('role', 'log');
  log.setAttribute('aria-live', 'polite');
  const MAX_LINES = 400;
  function appendLine(message) {
    const stick = log.scrollTop + log.clientHeight >= log.scrollHeight - 8;
    const row = document.createElement('div');
    row.className = 'dev-line ' + (message.level || 'info');
    const time = document.createElement('span');
    time.className = 'time';
    time.textContent = message.time || '';
    const text = document.createElement('span');
    text.className = 'text';
    text.textContent = message.text || '';
    row.append(time, text);
    log.appendChild(row);
    while (log.childElementCount > MAX_LINES) log.firstElementChild.remove();
    if (stick) log.scrollTop = log.scrollHeight;
  }

  const developer = () => (L.state && L.state.developer) || { status: '', pid: '' };

  function logPanel() {
    const dev = developer();
    return `<section class="dev-panel dev-log" aria-labelledby="dev-log-title">
      <div class="dev-log-head"><h2 id="dev-log-title">运行日志</h2><span class="mono">log/launcher.log</span><span class="grow"></span>
        <button type="button" class="btn small" data-action="dev.clear">清空</button>
        <button type="button" class="btn small" data-send="open.logs">日志目录</button></div>
      <div id="dev-log-slot" class="dev-log-slot"></div>
      <div class="dev-log-foot"><span class="state"><span class="dot ok" aria-hidden="true"></span>${esc(dev.status)}</span><span class="grow"></span>
        ${dev.pid ? `<span class="mono">PID ${esc(dev.pid)}</span>` : ''}<span class="mono">${esc(dev.momoko)}</span></div></section>`;
  }

  function serverPanel(server) {
    const endpoints = server.endpoints || {};
    const dev = developer();
    const method = server.developer ? server.developer.loginMethod : 'passport';
    const idle = server.status === 'idle';
    const loc = server.localization;
    const overridden = (key, momokoKey) => endpoints[key] !== endpoints[momokoKey];
    const row = (label, key, momokoKey) =>
      `<span class="k">${label}</span><span class="v${overridden(key, momokoKey) ? ' changed' : ''}" title="${esc(endpoints[key])}">${esc(endpoints[key] || '官方')}</span>`;
    return `<div class="card dev-panel">
      <div class="dev-card-head"><span>本服务器 · <b>${method === 'password' ? '账号密码登录' : '通行证登录'}</b></span>
        <button type="button" class="link dev-link" data-action="ui.settings" data-tab="game">编辑</button></div>
      <div class="dev-endpoints"><span class="k">应用</span><span class="v" title="${esc(server.id)}">${esc(server.id)}</span>${row('SDK', 'sdk', 'momokoSdk')}${row('游戏', 'game', 'momokoGame')}${row('资源', 'resource', 'momokoResource')}</div>
      <label class="dev-switch" for="dev-console-quick"><span>客户端控制台</span>${sw('dev-console-quick', dev.clientConsole, !idle, 'data-change="dev.console" data-key="value"')}</label>
      ${loc ? `<label class="dev-switch" for="dev-loc-quick"><span>汉化</span>${sw('dev-loc-quick', loc.enabled, !loc.package || !idle, `data-change="game.localization" data-game="${esc(server.game)}"`)}</label>` : ''}
      <div class="buttons"><button type="button" class="btn" data-send="dev.loginOnly" data-id="${esc(server.id)}"${attr('disabled', !idle || (!server.player && method !== 'password'))}>仅登录</button>
        <button type="button" class="btn" data-send="dev.cancel"${attr('disabled', server.status !== 'launching')}>取消操作</button></div></div>`;
  }

  // ---------- settings ----------
  function inputRow(id, label, value, placeholder, data) {
    return `<div class="item input-row"><label for="${id}">${label}</label>
      <input id="${id}" class="field" spellcheck="false" value="${esc(value)}" placeholder="${esc(placeholder)}" ${data}></div>`;
  }
  const divider = '<span class="divider" aria-hidden="true"></span>';

  function generalSections(state) {
    const dev = state.developer || {};
    const own = dev.momoko !== dev.configMomoko ? dev.momoko : '';
    return section('开发', `<div class="group">${inputRow('dev-momoko', 'Momoko', own, dev.configMomoko, 'data-change="dev.momoko"')}</div>
      <p class="dev-note">留空使用内置地址（构建参数 MomokoOrigin）。更换后需要重新登录通行证。</p>`, true);
  }

  function gameSections(server, state) {
    const endpoints = server.endpoints || {};
    const own = (key, momokoKey) => (endpoints[key] !== endpoints[momokoKey] ? endpoints[key] : '');
    const data = `data-change="dev.endpoints" data-id="${esc(server.id)}"`;
    const servers = section('服务器', `<div class="group">
        ${inputRow('dev-sdk', 'SDK', own('sdk', 'momokoSdk'), endpoints.momokoSdk, data)}${divider}
        ${inputRow('dev-game', '游戏服务端', own('game', 'momokoGame'), endpoints.momokoGame, data)}${divider}
        ${inputRow('dev-resource', '资源服务器', own('resource', 'momokoResource'), endpoints.momokoResource || '官方资源', data)}</div>
      <p class="dev-note">留空使用 Momoko 下发的地址。</p>`, true);

    const developer = server.developer || { loginMethod: 'passport', account: '', passwordSaved: false };
    const password = developer.loginMethod === 'password';
    const methods = [['passport', '通行证'], ['password', '账号密码（测试）']].map(([key, label]) =>
      `<button type="button" aria-pressed="${developer.loginMethod === key}" data-send="dev.loginMethod" data-id="${esc(server.id)}" data-value="${key}">${label}</button>`).join('');
    const credentials = password ? `<form class="group dev-credentials" data-submit="dev.password" data-id="${esc(server.id)}" autocomplete="off">
        ${inputRow('dev-account', '游戏账号', developer.account, '', '')}${divider}
        <div class="item input-row"><label for="dev-password">密码</label><input id="dev-password" class="field" type="password" data-keep
          placeholder="${developer.passwordSaved ? '已保存，重新输入可覆盖' : ''}">
          <button type="submit" class="btn">保存</button></div></form>` : '';
    const login = section('游戏登录方式', `<div class="seg full" role="group" aria-label="游戏登录方式">${methods}</div>${credentials}
      <p class="dev-note">${password ? '直接用游戏账号密码登录 SDK，不经过 Momoko。只有以 -tags dev 编译的游戏服务器提供这个入口。密码保存在 Windows 凭据管理器。' : '用应用 ID 向 Momoko 申请启动票据，再换取游戏会话。'}</p>`, true);

    const dev = state.developer || {};
    const debug = section('调试', `<div class="group">
        <label class="item" for="dev-console"><span class="label"><span>客户端控制台</span><small>Unity / Hook 日志窗口</small></span>
          ${sw('dev-console', dev.clientConsole, false, 'data-change="dev.console" data-key="value"')}</label>${divider}
        ${inputRow('dev-font', '汉化字体', dev.font, '', 'data-change="dev.font"')}${divider}
        ${inputRow('dev-args', '启动参数', dev.arguments, '空格分隔', 'data-change="dev.args"')}</div>`, true);
    return servers + login + debug;
  }

  L.extend({
    wide: true,
    badge: '<span class="badge-dev">DEV</span>',
    foot: '更改会自动保存 · 蓝色分组只编译进开发版',
    stage: (server) => (server ? logPanel() : ''),
    panel: (server) => (server ? serverPanel(server) : ''),
    general: generalSections,
    game: gameSections,
    mounted: () => {
      const slot = document.getElementById('dev-log-slot');
      if (slot && log.parentElement !== slot) {
        slot.appendChild(log);
        log.scrollTop = log.scrollHeight;
      }
    },
    messages: (message) => { if (message.type === 'log') appendLine(message); },
    actions: {
      'dev.clear': () => { log.textContent = ''; }
    },
    changes: {
      'dev.endpoints': (el) => send('dev.endpoints', {
        id: el.dataset.id,
        sdk: document.getElementById('dev-sdk').value,
        game: document.getElementById('dev-game').value,
        resource: document.getElementById('dev-resource').value
      })
    },
    submits: {
      'dev.password': (form) => {
        const input = document.getElementById('dev-password');
        send('dev.password', { id: form.dataset.id, account: document.getElementById('dev-account').value, password: input.value });
        input.value = '';
      }
    }
  });
  send('dev.replay');
})();
