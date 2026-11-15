const state = {
    tab: 'overview',
    account: { q: '', banned: '', page: 1, size: 25 },
    db: { type: 'items', q: '', page: 1, size: 25 },
    status: null
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => Array.from(document.querySelectorAll(selector));

async function api(path, options = {}) {
    const method = options.method || 'GET';
    const response = await fetch(path, {
        method,
        credentials: 'same-origin',
        headers: {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
        },
        body: options.body === undefined ? undefined : JSON.stringify(options.body)
    });

    if (response.status === 401) {
        location.href = '/login';
        throw new Error('未登录');
    }

    const text = await response.text();
    let data = null;
    if (text) {
        try { data = JSON.parse(text); } catch { data = text; }
    }

    if (!response.ok) {
        throw new Error(data && data.message ? data.message : `HTTP ${response.status}`);
    }

    return data;
}

function esc(value) {
    return String(value === null || value === undefined ? '' : value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function fmtDate(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return esc(value);
    if (date.getFullYear() > 9000) return '永久';
    return date.toLocaleString('zh-CN');
}

function fmtDuration(seconds) {
    seconds = Math.max(0, Number(seconds) || 0);
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (days > 0) return `${days}天 ${hours}小时`;
    if (hours > 0) return `${hours}小时 ${minutes}分`;
    return `${minutes}分 ${seconds % 60}秒`;
}

let toastTimer = null;
function toast(message, isError = false) {
    let node = $('#toast');
    if (!node) {
        node = document.createElement('div');
        node.id = 'toast';
        document.body.appendChild(node);
    }
    node.textContent = message;
    node.className = `toast show${isError ? ' error' : ''}`;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { node.className = 'toast'; }, 3200);
}

function showTab(name) {
    state.tab = name;
    $$('.nav').forEach(button => button.classList.toggle('active', button.dataset.tab === name));
    $$('.tab').forEach(section => section.classList.toggle('active', section.id === `tab-${name}`));

    if (name === 'overview') loadStatus();
    if (name === 'accounts') loadAccounts(1);
    if (name === 'players') loadPlayers();
    if (name === 'logs') loadLogs();
    if (name === 'database') loadDatabase();
    if (name === 'settings') loadConfig();
}

async function loadStatus() {
    try {
        const status = await api('/api/status');
        state.status = status;
        const badge = $('#serverBadge');
        badge.textContent = status.running ? '运行中' : '已停止';
        badge.className = `badge ${status.running ? 'ok' : 'off'}`;

        $('#statRunning').textContent = status.running ? '运行中' : '已停止';
        $('#statPlayers').textContent = status.players;
        $('#statAccounts').textContent = status.accounts;
        $('#statMaps').textContent = status.maps;
        $('#statMonsters').textContent = status.monsters;
        $('#statUptime').textContent = status.running ? fmtDuration(status.uptimeSeconds) : '-';
        $('#infoServer').textContent = `${status.serverIp}:${status.serverPort}`;
        $('#infoWeb').textContent = status.webAddress;
        $('#infoVersion').textContent = `DB v${status.dbVersion}（载入 v${status.loadedVersion}）`;
    } catch (error) {
        const badge = $('#serverBadge');
        badge.textContent = '连接失败';
        badge.className = 'badge off';
        console.error(error);
    }
}

async function serverAction(action) {
    if (action === 'stop' && !confirm('确定停止服务端？在线玩家会被断开。')) return;
    try {
        const result = await api(`/api/server/${action}`, { method: 'POST' });
        toast(result.message || '操作成功');
        setTimeout(loadStatus, 1200);
    } catch (error) {
        toast(error.message, true);
    }
}

async function loadAccounts(page) {
    state.account.page = Math.max(1, page || 1);
    const params = new URLSearchParams({ page: state.account.page, pageSize: state.account.size });
    if (state.account.q) params.set('q', state.account.q);
    if (state.account.banned) params.set('banned', state.account.banned);

    try {
        const data = await api(`/api/accounts?${params}`);
        const body = $('#accountsBody');
        if (!data.items.length) {
            body.innerHTML = '<tr><td colspan="8" class="muted">没有匹配的账号</td></tr>';
        } else {
            body.innerHTML = data.items.map(account => `
                <tr>
                    <td>${account.index}</td>
                    <td>${esc(account.accountId)}${account.admin ? ' <span class="tag">管理员</span>' : ''}</td>
                    <td>${esc(account.userName)}</td>
                    <td>${account.banned ? `<span class="bad">封禁</span>` : `<span class="good">正常</span>`}
                        ${account.banned && account.banReason ? `<div class="muted">${esc(account.banReason)}</div>` : ''}
                        ${account.banned ? `<div class="muted">至 ${fmtDate(account.expiryDate)}</div>` : ''}</td>
                    <td>${account.characterCount}</td>
                    <td>${esc(account.lastIp)}</td>
                    <td>${fmtDate(account.lastDate)}</td>
                    <td class="actions-cell">
                        <button data-account-action="detail" data-index="${account.index}">详情</button>
                        ${account.banned
                            ? `<button data-account-action="unban" data-index="${account.index}">解封</button>`
                            : `<button data-account-action="ban" data-index="${account.index}" data-account="${esc(account.accountId)}">封禁</button>`}
                        <button data-account-action="password" data-index="${account.index}" data-account="${esc(account.accountId)}">改密</button>
                        <button class="danger" data-account-action="delete" data-index="${account.index}" data-account="${esc(account.accountId)}">删除</button>
                    </td>
                </tr>
            `).join('');
        }

        renderPager($('#accountsPager'), data.total, data.page, data.pageSize, loadAccounts);
    } catch (error) {
        toast(error.message, true);
    }
}

function renderPager(container, total, page, pageSize, onPage) {
    const pages = Math.max(1, Math.ceil(total / pageSize));
    container.innerHTML = '';
    const previous = document.createElement('button');
    previous.textContent = '上一页';
    previous.disabled = page <= 1;
    previous.onclick = () => onPage(page - 1);

    const next = document.createElement('button');
    next.textContent = '下一页';
    next.disabled = page >= pages;
    next.onclick = () => onPage(page + 1);

    const info = document.createElement('span');
    info.textContent = `第 ${page} / ${pages} 页，共 ${total} 条`;

    container.append(previous, info, next);
}

async function accountAction(action, index, accountId) {
    try {
        if (action === 'detail') {
            const detail = await api(`/api/accounts/${index}`);
            const characters = detail.characters.length
                ? `<table class="data-table compact"><thead><tr><th>ID</th><th>角色</th><th>等级</th><th>职业</th></tr></thead><tbody>${detail.characters.map(c => `<tr><td>${c.index}</td><td>${esc(c.name)}</td><td>${c.level}</td><td>${esc(c.className)}</td></tr>`).join('')}</tbody></table>`
                : '<p class="muted">没有角色</p>';
            $('#accountDetail').innerHTML = `
                <table class="data-table compact">
                    <tbody>
                        <tr><th>账号</th><td>${esc(detail.accountId)}</td></tr>
                        <tr><th>用户名</th><td>${esc(detail.userName)}</td></tr>
                        <tr><th>邮箱</th><td>${esc(detail.email)}</td></tr>
                        <tr><th>状态</th><td>${detail.banned ? '封禁' : '正常'}</td></tr>
                        <tr><th>封禁原因</th><td>${esc(detail.banReason)}</td></tr>
                        <tr><th>封禁到期</th><td>${fmtDate(detail.expiryDate)}</td></tr>
                        <tr><th>创建 IP</th><td>${esc(detail.creationIp)}</td></tr>
                        <tr><th>创建时间</th><td>${fmtDate(detail.creationDate)}</td></tr>
                        <tr><th>最后 IP</th><td>${esc(detail.lastIp)}</td></tr>
                        <tr><th>最后登录</th><td>${fmtDate(detail.lastDate)}</td></tr>
                        <tr><th>金币 / 元宝</th><td>${detail.gold} / ${detail.credit}</td></tr>
                        <tr><th>仓库密码</th><td>${detail.hasStoragePassword ? '已设置' : '未设置'}</td></tr>
                    </tbody>
                </table>
                <h3>角色列表</h3>${characters}`;
            $('#accountDialog').showModal();
            return;
        }

        if (action === 'ban') {
            const reason = prompt(`封禁账号 ${accountId}，请输入原因：`, '违规');
            if (reason === null) return;
            const daysText = prompt('封禁天数（0 表示永久）：', '0');
            if (daysText === null) return;
            const days = parseInt(daysText, 10);
            const result = await api(`/api/accounts/${index}/ban`, { method: 'POST', body: { reason, days: Number.isFinite(days) ? days : 0 } });
            toast(result.message);
        }

        if (action === 'unban') {
            if (!confirm(`确定解封账号 ${accountId}？`)) return;
            const result = await api(`/api/accounts/${index}/unban`, { method: 'POST' });
            toast(result.message);
        }

        if (action === 'password') {
            const password = prompt(`为账号 ${accountId} 设置新密码（5-15 位字母数字）：`);
            if (!password) return;
            const result = await api(`/api/accounts/${index}/password`, { method: 'POST', body: { password } });
            toast(result.message);
        }

        if (action === 'delete') {
            if (!confirm(`确定删除账号 ${accountId}？角色和仓库数据会一起删除，且不可恢复。`)) return;
            const result = await api(`/api/accounts/${index}`, { method: 'DELETE' });
            toast(result.message);
        }

        loadAccounts(state.account.page);
    } catch (error) {
        toast(error.message, true);
    }
}

async function loadPlayers() {
    try {
        const players = await api('/api/players');
        const body = $('#playersBody');
        if (!players.length) {
            body.innerHTML = '<tr><td colspan="9" class="muted">当前没有在线玩家</td></tr>';
            return;
        }

        body.innerHTML = players.map(player => `
            <tr>
                <td>${esc(player.name)}${player.isGm ? ' <span class="tag">GM</span>' : ''}${player.dead ? ' <span class="muted">(死亡)</span>' : ''}</td>
                <td>${esc(player.accountId)}</td>
                <td>${player.level}</td>
                <td>${esc(player.className)}</td>
                <td>${esc(player.map)}</td>
                <td>${player.x},${player.y}</td>
                <td>${player.hp}/${player.mp}</td>
                <td>${esc(player.ip)}</td>
                <td class="actions-cell">
                    <button data-player-action="message" data-player="${esc(player.name)}">私聊</button>
                    <button class="danger" data-player-action="kick" data-player="${esc(player.name)}">踢下线</button>
                </td>
            </tr>
        `).join('');
    } catch (error) {
        toast(error.message, true);
    }
}

async function playerAction(action, name) {
    try {
        if (action === 'kick') {
            if (!confirm(`确定把玩家 ${name} 踢下线？`)) return;
            const result = await api(`/api/players/${encodeURIComponent(name)}/kick`, { method: 'POST' });
            toast(result.message);
        }

        if (action === 'message') {
            const message = prompt(`发送给 ${name} 的消息：`);
            if (!message) return;
            const result = await api('/api/players/message', { method: 'POST', body: { name, message } });
            toast(result.message);
        }

        loadPlayers();
    } catch (error) {
        toast(error.message, true);
    }
}

async function loadLogs() {
    const type = $('#logType').value;
    const lines = $('#logLines').value;
    $('#logContent').textContent = '加载中...';
    try {
        const data = await api(`/api/logs?type=${encodeURIComponent(type)}&lines=${encodeURIComponent(lines)}`);
        $('#logFile').textContent = data.file ? `当前文件：${data.file}` : '暂无日志文件';
        $('#logContent').textContent = data.lines.length ? data.lines.join('\n') : '暂无日志';
        $('#logContent').scrollTop = $('#logContent').scrollHeight;
    } catch (error) {
        $('#logContent').textContent = error.message;
    }
}

const dbColumns = {
    items: [
        ['index', 'ID'], ['name', '名称'], ['type', '类型'], ['grade', '品质'], ['price', '价格']
    ],
    monsters: [
        ['index', 'ID'], ['name', '名称'], ['gameName', '显示名'], ['level', '等级']
    ],
    maps: [
        ['index', 'ID'], ['fileName', '文件名'], ['title', '标题']
    ],
    npcs: [
        ['index', 'ID'], ['fileName', '文件名'], ['name', '名称'], ['mapIndex', '地图 ID']
    ]
};

async function loadDatabase() {
    try {
        const summary = await api('/api/db/summary');
        $('#dbItems').textContent = summary.items;
        $('#dbMonsters').textContent = summary.monsters;
        $('#dbMaps').textContent = summary.maps;
        $('#dbNpcs').textContent = summary.npcs;
        $('#dbQuests').textContent = summary.quests;
        $('#dbGuilds').textContent = summary.guilds;
    } catch (error) {
        toast(error.message, true);
    }

    loadDbTable(state.db.type, 1);
}

async function loadDbTable(type, page) {
    state.db.type = type;
    state.db.page = Math.max(1, page || 1);
    const params = new URLSearchParams({ page: state.db.page, pageSize: state.db.size });
    if (state.db.q) params.set('q', state.db.q);

    try {
        const data = await api(`/api/db/${type}?${params}`);
        const columns = dbColumns[type];
        $('#dbHead').innerHTML = `<tr>${columns.map(column => `<th>${column[1]}</th>`).join('')}</tr>`;

        if (!data.items.length) {
            $('#dbBody').innerHTML = `<tr><td colspan="${columns.length}" class="muted">没有匹配的数据</td></tr>`;
        } else {
            $('#dbBody').innerHTML = data.items.map(item => `<tr>${columns.map(column => `<td>${esc(item[column[0]])}</td>`).join('')}</tr>`).join('');
        }

        renderPager($('#dbPager'), data.total, data.page, data.pageSize, (nextPage) => loadDbTable(type, nextPage));
    } catch (error) {
        toast(error.message, true);
    }
}

async function loadConfig() {
    try {
        const config = await api('/api/config');
        const rows = [
            ['语言', config.general.language],
            ['多线程', config.general.multithreaded ? '开启' : '关闭'],
            ['怪物线程数', config.general.threadLimit],
            ['自动存档间隔（分钟）', config.general.saveDelay],
            ['测试模式', config.general.testServer ? '是' : '否'],
            ['监听 IP', config.network.ipAddress],
            ['游戏端口', config.network.port],
            ['最大玩家数', config.network.maxUser],
            ['同 IP 最大连接', config.network.maxIp],
            ['连接超时（毫秒）', config.network.timeout],
            ['HTTP 服务', config.network.startHttpService ? '开启' : '关闭'],
            ['Envir 目录', config.paths.envir],
            ['Maps 目录', config.paths.maps],
            ['Configs 目录', config.paths.configs]
        ];
        $('#configTable').innerHTML = rows.map(([label, value]) => `<tr><th>${esc(label)}</th><td>${esc(value)}</td></tr>`).join('');
    } catch (error) {
        toast(error.message, true);
    }
}

async function changePassword(event) {
    event.preventDefault();
    const currentPassword = $('#currentPassword').value;
    const newPassword = $('#newPassword').value;
    const confirmPassword = $('#confirmPassword').value;

    if (newPassword.length < 8) return toast('新密码至少 8 位', true);
    if (newPassword !== confirmPassword) return toast('两次输入的新密码不一致', true);

    try {
        const result = await api('/api/admin/password', { method: 'POST', body: { currentPassword, newPassword } });
        toast(result.message);
        $('#pwForm').reset();
        $('#defaultPasswordWarning')?.remove();
    } catch (error) {
        toast(error.message, true);
    }
}

document.addEventListener('DOMContentLoaded', () => {
    $$('.nav').forEach(button => button.addEventListener('click', () => showTab(button.dataset.tab)));
    $$('.subnav').forEach(button => button.addEventListener('click', () => {
        $$('.subnav').forEach(item => item.classList.toggle('active', item === button));
        state.db.q = '';
        $('#dbQ').value = '';
        loadDbTable(button.dataset.db, 1);
    }));

    $('#btnStart').addEventListener('click', () => serverAction('start'));
    $('#btnStop').addEventListener('click', () => serverAction('stop'));
    $('#btnRefreshStatus').addEventListener('click', loadStatus);

    $('#btnSearchAccounts').addEventListener('click', () => {
        state.account.q = $('#accountQ').value.trim();
        state.account.banned = $('#accountBanned').value;
        loadAccounts(1);
    });
    $('#accountQ').addEventListener('keydown', event => {
        if (event.key === 'Enter') $('#btnSearchAccounts').click();
    });
    $('#accountsBody').addEventListener('click', event => {
        const button = event.target.closest('button[data-account-action]');
        if (!button) return;
        accountAction(button.dataset.accountAction, Number(button.dataset.index), button.dataset.account);
    });

    $('#btnRefreshPlayers').addEventListener('click', loadPlayers);
    $('#playersBody').addEventListener('click', event => {
        const button = event.target.closest('button[data-player-action]');
        if (!button) return;
        playerAction(button.dataset.playerAction, button.dataset.player);
    });
    $('#btnBroadcast').addEventListener('click', async () => {
        const message = $('#broadcastMessage').value.trim();
        if (!message) return toast('请输入公告内容', true);
        try {
            const result = await api('/api/players/message', { method: 'POST', body: { message } });
            toast(result.message);
            $('#broadcastMessage').value = '';
        } catch (error) {
            toast(error.message, true);
        }
    });

    $('#btnRefreshLogs').addEventListener('click', loadLogs);
    $('#logType').addEventListener('change', loadLogs);
    $('#logLines').addEventListener('change', loadLogs);

    $('#btnSearchDb').addEventListener('click', () => {
        state.db.q = $('#dbQ').value.trim();
        loadDbTable(state.db.type, 1);
    });
    $('#dbQ').addEventListener('keydown', event => {
        if (event.key === 'Enter') $('#btnSearchDb').click();
    });

    $('#pwForm').addEventListener('submit', changePassword);
    $('#btnCloseDialog').addEventListener('click', () => $('#accountDialog').close());

    const initialTab = location.hash ? location.hash.slice(1) : 'overview';
    showTab($$(`.nav[data-tab="${initialTab}"]`).length ? initialTab : 'overview');
    setInterval(loadStatus, 5000);
});
