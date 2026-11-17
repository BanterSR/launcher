'use strict';
// Momoko launcher UI. The host posts one "state" message with the whole view
// model; this file renders it and sends user actions back. Dialog and sheet
// visibility is local UI state. Developer panels live in dev/ and are only
// shipped and loaded in the developer build.
(() => {
  const host = window.chrome && window.chrome.webview;
  const send = (type, data) => { if (host) host.postMessage(Object.assign({ type }, data || {})); };

  const SPARK = 'M12 2C12.8 8 16 11.2 22 12C16 12.8 12.8 16 12 22C11.2 16 8 12.8 2 12C8 11.2 11.2 8 12 2Z';
  const GEAR = 'M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z';
  const icons = {
    spark: `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${SPARK}"/></svg>`,
    gear: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="${GEAR}"/></svg>`,
    play: '<svg viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" aria-hidden="true"><path d="M7 5v14l11-7z"/></svg>',
    close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>',
    shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3l7 3v5c0 4.5-3 8.3-7 9.5C8 19.3 5 15.5 5 11V6z"/><path d="M9 11.5l2 2 4-4"/></svg>',
    refresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 11a8 8 0 1 0-2.3 5.7"/><path d="M20 4v7h-7"/></svg>',
    folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>',
    download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11"/><path d="M7 10l5 5 5-5"/><path d="M5 20h14"/></svg>'
  };

  const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const attr = (name, on) => (on ? ` ${name}` : '');

  // Developer build hooks (dev/dev.js). Each list holds functions returning HTML.
  const ext = { stage: [], panel: [], general: [], game: [], mounted: [], messages: [], actions: {}, changes: {}, submits: {}, wide: false, badge: '', foot: '' };

  let state = null;
  const ui = { settings: null, dialog: null, pending: null, view: '', licenses: null };
  const $ = (id) => document.getElementById(id);

  // ---------- theme ----------
  const media = matchMedia('(prefers-color-scheme: dark)');
  const initialTheme = new URLSearchParams(location.search).get('theme');
  document.documentElement.dataset.theme = initialTheme === 'light' ? 'light' : 'dark';
  function applyTheme() {
    if (!state) return;
    const dark = state.theme === 'dark' || (state.theme === 'system' && media.matches);
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  }
  media.addEventListener('change', () => {
    applyTheme();
    send('theme.system', { dark: media.matches });
  });

  // ---------- DOM patching ----------
  // Replaces a region only when its markup changed and keeps what the user is
  // typing: inputs marked data-keep and the focused input survive a re-render.
  function patch(el, html) {
    if (el._html === html) return false;
    const keep = [];
    let focus = null;
    el.querySelectorAll('input[id]').forEach((input) => {
      const focused = document.activeElement === input;
      if (input.type !== 'checkbox' && (input.hasAttribute('data-keep') || focused)) keep.push([input.id, input.value]);
      if (focused) focus = [input.id, input.selectionStart, input.selectionEnd];
    });
    el.innerHTML = html;
    el._html = html;
    for (const [id, value] of keep) {
      const input = el.querySelector('#' + CSS.escape(id));
      if (input) input.value = value;
    }
    if (focus) {
      const input = el.querySelector('#' + CSS.escape(focus[0]));
      if (input && !input.disabled) {
        input.focus();
        try { input.setSelectionRange(focus[1], focus[2]); } catch { /* not a text input */ }
      }
    }
    return true;
  }

  // ---------- helpers ----------
  const current = () => {
    const list = state.servers || [];
    return list.find((server) => server.id === state.selected) || list[0] || null;
  };
  const serverState = (server) => {
    switch (server.state) {
      case 'running': return ['ok', server.online >= 0 ? `运行中 · 在线 ${server.online}` : '运行中'];
      case 'maintenance': return ['warn', '维护中'];
      case 'starting': return ['warn', '启动中'];
      case 'stopping': return ['warn', '停止中'];
      case 'stopped': return ['bad', '已停止'];
      case 'unreachable': return ['bad', '无法连接'];
      default: return ['', '状态未知'];
    }
  };
  const blockText = (reason) => ({
    unsupported: '这台服务器不支持从启动器绑定或新建游戏账号。',
    not_visible: '这台服务器没有向你的通行证开放。',
    cooldown: '刚解绑过这台服务器的游戏账号，冷却期内只能重新绑定原来的账号。'
  }[reason] || '暂时不能在这台服务器上绑定游戏账号。');
  const avatar = (account, large) => {
    const cls = large ? 'avatar large' : 'avatar';
    if (account && account.avatar) return `<span class="${cls}"><img src="${esc(account.avatar)}" alt=""></span>`;
    return `<span class="${cls}">${esc(account ? account.initial : '')}</span>`;
  };
  const sw = (id, checked, disabled, data) =>
    `<span class="switch"><input id="${id}" type="checkbox" role="switch"${attr('checked', checked)}${attr('disabled', disabled)} ${data}><span class="track" aria-hidden="true"></span></span>`;
  const section = (title, body, dev) => `<section class="section"><h3${dev ? ' class="dev"' : ''}>${esc(title)}</h3>${body}</section>`;
  // A newer launcher release on GitHub (user build only), or null.
  const newer = () => (state.update && state.update.latest ? state.update : null);

  // ---------- passport screens ----------
  function passportBackdrop() {
    return `<div class="backdrop-dots" aria-hidden="true">
      <svg class="spark-big" viewBox="0 0 24 24"><path d="${SPARK}"/></svg>
      <svg class="spark-line" viewBox="0 0 24 24"><path d="${SPARK}"/></svg></div>`;
  }

  function passportCard() {
    const update = newer();
    const head = `<span class="logo" aria-hidden="true">${icons.spark}</span><h1 class="brand">MOMOKO</h1><p class="brand-sub">游戏启动器</p>${update
      ? `<div class="links"><button type="button" class="link update-link" data-send="update.open">发现新版本 ${esc(update.latest)}，前往下载</button></div>` : ''}${announcement()}`;
    const login = state.login || {};
    const busy = !!login.busy;
    const error = `<p class="form-error" role="alert">${esc(login.error || '')}</p>`;
    if (state.phase === 'starting') {
      return `<div class="passport">${head}<div class="status-line"><span class="spinner" aria-hidden="true"></span>正在连接 Momoko 通行证…</div></div>`;
    }
    if (state.phase === 'offline') {
      const who = state.account ? `<p class="form-hint">当前通行证：${esc(state.account.name)}</p>` : '';
      return `<div class="passport">${head}
        <p class="notice">${esc(state.notice || '无法连接 Momoko 通行证。')}</p>
        <div class="form">${who}<button type="button" class="primary" data-send="auth.retry">${icons.refresh}重试</button></div>
        <div class="links"><button type="button" class="link" data-action="ui.logout">退出登录</button></div></div>`;
    }
    if (login.code) {
      return `<div class="passport">${head}
        <p class="form-hint auth-hint">已在浏览器中打开 Momoko 通行证。登录后核对下面的验证码，与浏览器中一致时点击「授权」。</p>
        <div class="auth-code" aria-label="验证码">${esc(login.code)}</div>
        <div class="status-line"><span class="spinner" aria-hidden="true"></span>等待浏览器授权…</div>
        <div class="form"><button type="button" class="btn" data-send="auth.reopen">重新打开浏览器</button></div>
        <div class="links"><button type="button" class="link" data-send="auth.cancel">取消</button></div></div>`;
    }
    return `<div class="passport">${head}
      <div class="form">
        <p class="form-hint">在浏览器中登录 Momoko 通行证，并授权这台电脑。</p>
        <button type="button" class="primary" data-send="auth.start"${attr('disabled', busy)}>${busy ? '<span class="spinner" aria-hidden="true"></span>正在打开浏览器' : `${icons.shield}登录通行证`}</button>
        ${error}
      </div>
      <div class="links"><button type="button" class="link" data-send="open.register">注册通行证</button><span aria-hidden="true">·</span><button type="button" class="link" data-send="open.forgot">忘记密码</button></div></div>`;
  }

  // ---------- game page ----------
  // Backgrounds from the Momoko launcher settings: one fills the stage, two or
  // three sit side by side as slanted panels.
  function artView(server) {
    const art = server ? server.art.slice(0, 3) : [];
    const images = art.map((src) => `<img src="${esc(src)}" alt="">`).join('');
    return `<div class="art${art.length === 1 ? ' single' : ''}" aria-hidden="true">${images}<div class="scrim-b"></div><div class="scrim-l"></div></div>`;
  }

  // Server icon from Momoko (the square image of the server's cover); without
  // one the rail shows the game's initial.
  const gameIcon = (item) => (item.icon
    ? `<img src="${esc(item.icon)}" alt="">`
    : `<span class="initial">${esc((item.title || item.game || '?').charAt(0).toUpperCase())}</span>`);

  const announcement = () => (state.announcement
    ? `<p class="announcement" role="note">${esc(state.announcement)}</p>` : '');

  function railView(server) {
    const games = (state.servers || []).map((item) => `<button type="button" class="game" data-send="server.select" data-id="${esc(item.id)}"
        data-tip="${esc(item.title)}" data-tip-sub="${esc(item.name)}" aria-label="${esc(item.title + ' · ' + item.name)}"${server && item.id === server.id ? ' aria-current="page"' : ''}>
        ${gameIcon(item)}</button>`).join('');
    return `<nav class="rail" aria-label="游戏">
      <span class="mark" aria-hidden="true">${icons.spark}</span><span class="sep" aria-hidden="true"></span>
      <div class="games">${games}</div>
      <div class="fill"></div>${newer()
        ? `<button type="button" class="icon update" data-send="update.open" data-tip="发现新版本 ${esc(newer().latest)}" data-tip-sub="前往 GitHub 下载" aria-label="下载新版本">${icons.download}</button>` : ''}
      <button type="button" class="icon" data-send="server.refresh" data-tip="刷新服务器列表" aria-label="刷新服务器列表">${icons.refresh}</button>
      <button type="button" class="icon" data-action="ui.settings" data-tab="general" data-tip="设置" aria-label="设置">${icons.gear}</button>
      <button type="button" class="avatar-button" data-action="ui.settings" data-tab="general" data-tip="${esc(state.account ? state.account.name : '')}" data-tip-sub="Momoko 通行证" aria-label="通行证账号">${avatar(state.account)}</button>
    </nav>`;
  }

  function mainView(server) {
    if (!server) {
      return `<div class="heading"><span class="wordmark">MOMOKO</span><h1>还没有可启动的游戏</h1></div>
        <div class="actions"><div class="card row"><span class="beacon warn" aria-hidden="true"></span><span class="stack">
          <span class="title">通行证下没有可启动的服务器</span>
          <span class="body">服务器向你的通行证开放后会出现在左侧。也可以先在通行证网页里绑定游戏账号。</span></span></div>
          <div class="cta-row"><button type="button" class="btn" data-send="open.account">打开通行证</button><button type="button" class="btn accent" data-send="server.refresh">刷新</button></div></div>`;
    }
    const [tone, label] = serverState(server);
    const wordmark = server.wordmark ? `<span class="wordmark">${esc(server.wordmark)}</span>` : '';
    const heading = `<div class="heading">${wordmark}<h1>${esc(server.title)}</h1>
      <span class="server"><span class="dot ${tone}" aria-hidden="true"></span>${esc(server.name)}<small>${esc(label)}</small></span></div>`;
    const status = server.status;
    const player = server.player;
    // Developer build: the test password login reaches the SDK without a Momoko link.
    const canLaunch = !!player || !!(server.developer && server.developer.loginMethod === 'password');
    let card = '';
    if (status === 'running') {
      card = `<div class="card row" role="status"><span class="beacon" aria-hidden="true"></span><span class="stack"><span class="title">游戏运行中</span>
        <span class="body">退出游戏后会自动清理本次登录留下的文件，存档和资源缓存不受影响。关闭启动器会缩到后台，清理完成后自动退出。</span></span></div>`;
    } else if (status === 'cleaning') {
      card = `<div class="card row" role="status"><span class="beacon warn" aria-hidden="true"></span><span class="stack"><span class="title">正在清理</span>
        <span class="body">游戏已退出，正在删除本次登录留下的文件。</span></span></div>`;
    } else if (status === 'busy') {
      card = `<div class="card row" role="status"><span class="beacon warn" aria-hidden="true"></span><span class="stack"><span class="title">另一台服务器正在运行</span>
        <span class="body">同一个游戏一次只能运行一份，请先退出正在运行的游戏。</span></span></div>`;
    } else if (!player) {
      const canBind = server.bindable || server.blockReason === 'cooldown';
      const buttons = canBind
        ? `<div class="buttons"><button type="button" class="btn" data-action="ui.bind" data-id="${esc(server.id)}">绑定已有账号</button>${server.bindable
          ? `<button type="button" class="btn accent" data-action="ui.create" data-id="${esc(server.id)}">新建账号</button>` : ''}</div>`
        : '';
      card = `<div class="card"><span class="stack"><span class="title">还没有游戏账号</span>
        <span class="body">${server.bindable ? '在这台服务器上绑定已有的游戏账号，或者新建一个。账号会归属当前通行证。' : esc(blockText(server.blockReason))}</span></span>${buttons}</div>`;
    } else if (!server.executable.valid && canLaunch) {
      card = `<div class="card row"><span class="beacon warn" aria-hidden="true"></span><span class="stack"><span class="title">没有找到游戏程序</span>
        <span class="body">请选择 ${esc(server.executable.name)} 的位置。</span></span></div>`;
    }
    const pill = player && (status === 'idle' || status === 'launching')
      ? `<span class="pill"><span class="dot ok" aria-hidden="true"></span><span>玩家 ${esc(player.displayId || player.id)}${player.nickname ? ' · ' + esc(player.nickname) : ''}</span></span>` : '';
    let cta;
    if (status === 'launching') cta = '<button type="button" class="cta quiet" disabled><span class="spinner" aria-hidden="true"></span>正在启动</button>';
    else if (status === 'running') cta = '<button type="button" class="cta quiet" disabled>运行中</button>';
    else if (status === 'cleaning') cta = '<button type="button" class="cta quiet" disabled><span class="spinner" aria-hidden="true"></span>正在清理</button>';
    else if (status === 'busy') cta = '<button type="button" class="cta quiet small-text" disabled>运行中</button>';
    else if (!canLaunch) cta = `<button type="button" class="cta quiet" disabled>${icons.play}开始游戏</button>`;
    else if (!server.executable.valid) cta = `<button type="button" class="cta small-text" data-send="game.browse" data-game="${esc(server.game)}">${icons.folder}选择游戏程序</button>`;
    else cta = `<button type="button" class="cta" data-send="server.launch" data-id="${esc(server.id)}">${icons.play}开始游戏</button>`;
    const extra = ext.stage.map((fn) => fn(server, state)).join('');
    const panel = ext.panel.map((fn) => fn(server, state)).join('');
    return `${heading}${extra}<div class="actions">${announcement()}${panel}${card}${pill}<div class="cta-row">${cta}
      <button type="button" class="round" data-action="ui.settings" data-tab="game" aria-label="游戏设置">${icons.gear}</button></div></div>`;
  }

  // ---------- settings ----------
  function generalSections() {
    const themes = [['system', '跟随系统'], ['light', '浅色'], ['dark', '深色']].map(([key, label]) =>
      `<button type="button" aria-pressed="${state.theme === key}" data-send="theme.set" data-value="${key}">${label}</button>`).join('');
    const account = state.account;
    const list = [
      section('外观', `<div class="group"><div class="item"><span class="label"><span>主题</span></span><div class="seg" role="group" aria-label="主题">${themes}</div></div></div>`),
      section('通行证', `<div class="group"><div class="item">${avatar(account, true)}<span class="label"><span>${esc(account ? account.name : '')}</span>
          <small>${esc(account && account.username !== account.name ? account.username + ' · ' : '')}Momoko 通行证 · 所有游戏共用</small></span>
          <button type="button" class="btn" data-send="open.account">管理</button></div>
          <span class="divider" aria-hidden="true"></span><button type="button" class="item danger" data-action="ui.logout">退出登录</button></div>`),
      section('帮助', `<div class="group"><div class="item"><span class="label"><span>日志目录</span><small>遇到问题时，把 launcher.log 发给服务器维护者</small></span>
          <button type="button" class="btn" data-send="open.logs">打开</button></div>
          <span class="divider" aria-hidden="true"></span>
          <div class="item"><span class="label"><span>开源许可</span><small>启动器使用的第三方组件及其许可</small></span>
          <button type="button" class="btn" data-action="ui.licenses">查看</button></div></div>`),
      section('关于', aboutGroup())
    ];
    return list.concat(ext.general.map((fn) => fn(state)));
  }

  function aboutGroup() {
    const update = state.update || {};
    let detail;
    let button = '';
    if (!update.enabled) {
      detail = state.edition === 'dev' ? '开发版不检查更新' : '本地构建，不检查更新';
    } else if (update.checking) {
      detail = '正在检查更新…';
    } else if (update.latest) {
      detail = `发现新版本 ${update.latest}`;
      button = '<button type="button" class="btn accent" data-send="update.open">前往下载</button>';
    } else {
      detail = update.error ? `检查更新失败：${update.error}` : '已是最新版本';
      button = '<button type="button" class="btn" data-send="update.check">检查更新</button>';
    }
    return `<div class="group"><div class="item"><span class="label"><span>Momoko 启动器 ${esc(update.version || '')}</span>
      <small>${esc(detail)}</small></span>${button}</div></div>`;
  }

  function gameSections(server) {
    const exe = server.executable;
    const list = [
      section('游戏', `<div class="group"><div class="item"><span class="label"><span>游戏程序</span>
          <small class="mono" title="${esc(exe.path)}">${esc(exe.path)}</small>${exe.valid ? '' : `<small class="bad">找不到有效的 ${esc(exe.name)}，请重新选择。</small>`}</span>
          <button type="button" class="btn" data-send="game.browse" data-game="${esc(server.game)}">更改</button></div></div>`)
    ];
    const loc = server.localization;
    if (loc) {
      const pkg = loc.package;
      const running = server.status !== 'idle';
      list.push(section('汉化', `<div class="group"><label class="item" for="loc-${esc(server.game)}"><span class="label"><span>启用汉化</span>${pkg ? '' : '<small>先导入汉化包</small>'}</span>
          ${sw('loc-' + esc(server.game), loc.enabled, !pkg || running, `data-change="game.localization" data-game="${esc(server.game)}"`)}</label>
          <span class="divider" aria-hidden="true"></span>
          <div class="item"><span class="label"><span>汉化包</span><small>${pkg ? `${esc(pkg.name)} · ${esc(pkg.version)} · ${pkg.files} 个文件 · ${esc(pkg.importedAt)} 导入` : '还没有导入'}</small></span>
          <button type="button" class="btn" data-send="game.importLocalization" data-game="${esc(server.game)}"${attr('disabled', running)}>导入新包</button></div></div>`));
    }
    return list.concat(ext.game.map((fn) => fn(server, state)));
  }

  function settingsView(server) {
    const tab = ui.settings === 'game' && server ? 'game' : 'general';
    const tabs = [`<button type="button" role="tab" aria-selected="${tab === 'general'}" data-action="ui.settings" data-tab="general">通用</button>`];
    if (server) tabs.push(`<button type="button" role="tab" aria-selected="${tab === 'game'}" data-action="ui.settings" data-tab="game">${esc(server.title)}</button>`);
    const sections = tab === 'game' ? gameSections(server) : generalSections();
    let content;
    if (ext.wide) {
      const columns = [[], []];
      sections.forEach((item, index) => columns[index % 2].push(item));
      content = `<div class="columns">${columns.map((column) => `<div class="column">${column.join('')}</div>`).join('')}</div>`;
    } else {
      content = sections.join('');
    }
    return `<div class="scrim dim-overlay" data-dismiss><div class="sheet settings${ext.wide ? ' wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="settings-title">
      <div class="head"><h2 id="settings-title">设置${ext.badge}</h2><span class="grow"></span>
        <button type="button" class="close" data-action="ui.close" aria-label="关闭设置">${icons.close}</button></div>
      <div class="tabs"><div class="seg" role="tablist" aria-label="设置分类">${tabs.join('')}</div></div>
      <div class="content">${content}</div>
      <div class="foot">${esc(ext.foot || '更改会自动保存')}</div></div></div>`;
  }

  // ---------- dialogs ----------
  function dialogView() {
    const dialog = ui.dialog;
    const server = dialog.id ? (state.servers || []).find((item) => item.id === dialog.id) : null;
    const pending = ui.pending === dialog.kind;
    const wrap = (body) => `<div class="scrim modal" data-dismiss><div class="sheet dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title">${body}</div></div>`;
    if (dialog.kind === 'bind' && server) {
      return wrap(`<h2 id="dialog-title">绑定已有游戏账号</h2>
        <p>输入 ${esc(server.name)} 上的游戏账号和密码。绑定后这个游戏账号归属当前通行证。</p>
        <form class="form" data-submit="bind" autocomplete="off">
          <input id="bind-account" class="field" data-keep placeholder="游戏账号" aria-label="游戏账号" spellcheck="false"${attr('disabled', pending)}>
          <input id="bind-password" class="field" data-keep type="password" placeholder="游戏密码" aria-label="游戏密码"${attr('disabled', pending)}>
          <div class="row-buttons"><button type="button" class="btn" data-action="ui.close">取消</button>
            <button type="submit" class="btn accent"${attr('disabled', pending)}>${pending ? '正在绑定…' : '绑定'}</button></div>
        </form>`);
    }
    if (dialog.kind === 'create' && server) {
      return wrap(`<h2 id="dialog-title">新建游戏账号</h2>
        <p>将在 ${esc(server.name)} 上新建一个游戏账号，并绑定到当前通行证。</p>
        <div class="row-buttons"><button type="button" class="btn" data-action="ui.close">取消</button>
          <button type="button" class="btn accent" data-action="confirm.create" data-id="${esc(server.id)}"${attr('disabled', pending)}>${pending ? '正在创建…' : '新建'}</button></div>`);
    }
    if (dialog.kind === 'licenses') {
      return wrap(`<h2 id="dialog-title">开源许可</h2>
        <pre class="licenses">${esc(ui.licenses ?? '正在读取…')}</pre>
        <div class="row-buttons"><button type="button" class="btn" data-action="ui.close">关闭</button></div>`).replace('sheet dialog', 'sheet dialog wide-dialog');
    }
    if (dialog.kind === 'logout') {
      return wrap(`<h2 id="dialog-title">退出登录</h2>
        <p>会删除这台电脑上保存的通行证登录，下次打开启动器需要重新登录。游戏账号的绑定不受影响。</p>
        <div class="row-buttons"><button type="button" class="btn" data-action="ui.close">取消</button>
          <button type="button" class="btn danger" data-action="confirm.logout">退出登录</button></div>`);
    }
    return '';
  }

  // ---------- render ----------
  function render() {
    if (!state) return;
    applyTheme();
    const ready = state.phase === 'ready';
    if (!ready) ui.settings = null;
    if (ui.dialog && ui.dialog.kind !== 'logout' && !ready) ui.dialog = null;
    const server = ready ? current() : null;
    if (ui.dialog && ui.dialog.id && (!server || ui.dialog.id !== server.id || server.player)) { ui.dialog = null; ui.pending = null; }

    patch($('art-slot'), ready ? artView(server) : passportBackdrop());
    patch($('rail-slot'), ready ? railView(server) : '');
    patch($('main-slot'), ready ? mainView(server) : passportCard());
    patch($('overlay'), ui.dialog ? dialogView() : ui.settings ? settingsView(server) : '');
    $('stage').classList.toggle('dim', !!ui.settings && ready);

    const view = ready ? 'ready' : state.phase + (state.login && state.login.code ? '-code' : '');
    if (view !== ui.view) {
      ui.view = view;
      const first = document.querySelector('#main-slot input:not([disabled])');
      if (first) (first.value ? document.querySelector('#login-password') || first : first).focus();
    }
    ext.mounted.forEach((fn) => fn(server, state));
  }

  // ---------- toasts & tooltip ----------
  function toast(kind, text) {
    const box = $('toasts');
    const item = document.createElement('div');
    item.className = 'toast';
    item.innerHTML = `<span class="dot ${kind === 'ok' ? 'ok' : kind === 'error' ? 'bad' : 'warn'}" aria-hidden="true"></span><span></span>`;
    item.lastChild.textContent = text;
    box.appendChild(item);
    while (box.children.length > 3) box.firstChild.remove();
    setTimeout(() => { item.classList.add('leaving'); setTimeout(() => item.remove(), 220); }, kind === 'error' ? 6000 : 3800);
  }

  const tip = document.createElement('div');
  tip.className = 'tip';
  tip.setAttribute('aria-hidden', 'true');
  document.addEventListener('mouseover', (event) => {
    const target = event.target.closest('[data-tip]');
    if (!target || !target.dataset.tip) { tip.classList.remove('show'); return; }
    tip.innerHTML = `${esc(target.dataset.tip)}${target.dataset.tipSub ? `<small>${esc(target.dataset.tipSub)}</small>` : ''}`;
    const rect = target.getBoundingClientRect();
    tip.style.left = `${rect.right + 14}px`;
    tip.style.top = `${rect.top + rect.height / 2}px`;
    tip.classList.add('show');
  });

  // ---------- events ----------
  function closeOverlay() {
    if (ui.dialog) { ui.dialog = null; ui.pending = null; } else ui.settings = null;
    render();
  }

  const actions = {
    'ui.settings': (el) => { ui.settings = el.dataset.tab || 'general'; render(); },
    'ui.close': closeOverlay,
    'ui.bind': (el) => { ui.dialog = { kind: 'bind', id: el.dataset.id }; render(); const input = $('bind-account'); if (input) input.focus(); },
    'ui.create': (el) => { ui.dialog = { kind: 'create', id: el.dataset.id }; render(); },
    'ui.logout': () => { ui.dialog = { kind: 'logout' }; render(); },
    'ui.licenses': () => {
      ui.dialog = { kind: 'licenses' };
      render();
      if (ui.licenses == null) {
        fetch('THIRD_PARTY.md').then((r) => (r.ok ? r.text() : Promise.reject(new Error(String(r.status)))))
          .then((text) => { ui.licenses = text; }, (error) => { ui.licenses = '读取失败：' + error.message; })
          .then(render);
      }
    },
    'confirm.create': (el) => { ui.pending = 'create'; send('server.create', { id: el.dataset.id }); render(); },
    'confirm.logout': () => { ui.dialog = null; ui.settings = null; send('auth.logout'); render(); }
  };

  const submits = {
    bind: () => {
      ui.pending = 'bind';
      send('server.bind', { id: ui.dialog.id, account: $('bind-account').value, password: $('bind-password').value });
      render();
    }
  };

  let pressed = null;
  document.addEventListener('mousedown', (event) => { pressed = event.target; }, true);
  document.addEventListener('click', (event) => {
    if (event.target.hasAttribute && event.target.hasAttribute('data-dismiss')) {
      if (pressed === event.target) closeOverlay();
      return;
    }
    const el = event.target.closest('[data-send],[data-action]');
    if (!el || el.disabled) return;
    if (el.dataset.send) {
      const args = Object.assign({}, el.dataset);
      delete args.send; delete args.tip; delete args.tipSub;
      send(el.dataset.send, args);
      if (el.dataset.send === 'theme.set' && state) { state.theme = el.dataset.value; render(); }
      return;
    }
    const name = el.dataset.action;
    const handler = actions[name] || ext.actions[name];
    if (handler) handler(el, event);
  });

  document.addEventListener('change', (event) => {
    const el = event.target.closest('[data-change]');
    if (!el) return;
    const name = el.dataset.change;
    if (ext.changes[name]) { ext.changes[name](el, event); return; }
    const args = Object.assign({}, el.dataset);
    delete args.change; delete args.key;
    args[el.dataset.key || (el.type === 'checkbox' ? 'enabled' : 'value')] = el.type === 'checkbox' ? el.checked : el.value;
    send(name, args);
  });

  document.addEventListener('submit', (event) => {
    event.preventDefault();
    const name = event.target.dataset.submit;
    const handler = submits[name] || ext.submits[name];
    if (handler) handler(event.target, event);
  });

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && (ui.dialog || ui.settings)) { event.preventDefault(); closeOverlay(); }
  });

  document.addEventListener('load', (event) => {
    if (event.target.tagName === 'IMG') event.target.classList.add('loaded');
  }, true);

  // Fallback for runtimes without non-client region support: drag from JS.
  $('titlebar').addEventListener('mousedown', (event) => { if (event.button === 0) send('window.drag'); });

  // ---------- host messages ----------
  let devLoaded = false;
  function loadDeveloper() {
    devLoaded = true;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'dev/dev.css';
    document.head.appendChild(link);
    const script = document.createElement('script');
    script.src = 'dev/dev.js';
    document.head.appendChild(script);
  }

  if (host) {
    host.addEventListener('message', (event) => {
      const message = event.data;
      if (!message || typeof message !== 'object') return;
      if (message.type === 'state') {
        state = message;
        if (state.edition === 'dev' && !devLoaded) loadDeveloper();
        render();
        return;
      }
      if (message.type === 'toast') {
        toast(message.kind, message.text);
        if (ui.pending) { ui.pending = null; render(); }
        return;
      }
      ext.messages.forEach((fn) => fn(message));
    });
  }

  window.Launcher = {
    send, render, esc, attr, sw, section, icons, patch, toast,
    get state() { return state; },
    ui,
    extend(hooks) {
      for (const key of ['stage', 'panel', 'general', 'game', 'mounted', 'messages']) if (hooks[key]) ext[key].push(hooks[key]);
      for (const key of ['actions', 'changes', 'submits']) if (hooks[key]) Object.assign(ext[key], hooks[key]);
      if (hooks.wide) ext.wide = true;
      if (hooks.badge) ext.badge = hooks.badge;
      if (hooks.foot) ext.foot = hooks.foot;
      render();
    }
  };

  document.getElementById('stage').innerHTML = '<div id="art-slot"></div><div id="rail-slot"></div><div id="main-slot"></div>';
  document.getElementById('app').appendChild(tip);
  send('ready');
})();
