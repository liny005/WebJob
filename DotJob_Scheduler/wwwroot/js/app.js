// DotJob 前端应用 JavaScript
// 此文件处理所有前端 API 调用和 UI 交互

// 全局变量
let currentPage = 1;
let pageSize = 20;
let currentJobName = '';
let currentJobGroup = '';
let logCurrentPage = 1;
let logPageSize = 20;
let autoRefreshTimer = null;  // 自动刷新定时器
let currentUser = null;       // 当前登录用户信息

// 审计日志分页
let auditCurrentPage = 1;
let auditPageSize = 20;

// 当前生效的搜索条件（只有用户点击"搜索"按钮才更新）
let activeFilter = { jobName: '', jobGroup: '' };

// 页面加载初始化
document.addEventListener('DOMContentLoaded', function() {
    checkAuth();
    loadJobs();
    
    // 设置默认开始时间为当前时间
    const now = new Date();
    document.getElementById('beginTime').value =
        new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    
    // 不自动启动刷新，等用户勾选开关

    // ── 嵌套 Modal 修复 ──────────────────────────────────────────
    // Bootstrap 5 嵌套弹窗问题：
    // 1. 子弹窗关闭时，Bootstrap 会移除 body.modal-open，导致父弹窗背景滚动异常
    // 2. ESC 按键会同时关闭子弹窗和父弹窗
    // 解决方案：监听所有 modal 的 hide/hidden 事件，动态补回 modal-open class

    // 父-子关系：jobLogsModal → logDetailModal
    //            auditLog区域 → remarkDetailModal
    const childParentMap = {
        'logDetailModal':    'jobLogsModal',
        'remarkDetailModal': null   // remarkDetailModal 从 audit table 直接打开，无父 modal
    };

    // 子弹窗关闭后，若父弹窗仍然显示，重新加上 modal-open 保持背景锁定
    Object.entries(childParentMap).forEach(([childId, parentId]) => {
        const childEl = document.getElementById(childId);
        if (!childEl) return;
        childEl.addEventListener('hidden.bs.modal', () => {
            if (parentId) {
                const parentEl = document.getElementById(parentId);
                if (parentEl && parentEl.classList.contains('show')) {
                    document.body.classList.add('modal-open');
                }
            }
        });
    });

    // 防止 ESC 关闭父弹窗（仅当子弹窗可见时）
    document.addEventListener('keydown', function(e) {
        if (e.key !== 'Escape') return;
        // 如果 logDetailModal 可见，只关闭它，阻止事件继续影响 jobLogsModal
        const logDetailEl = document.getElementById('logDetailModal');
        if (logDetailEl && logDetailEl.classList.contains('show')) {
            bootstrap.Modal.getOrCreateInstance(logDetailEl).hide();
            e.stopImmediatePropagation();
        }
    }, true); // capture 阶段拦截，优先于 Bootstrap 处理
});

// 自动刷新开关切换
function onAutoRefreshToggle() {
    const sw = document.getElementById('autoRefreshSwitch');
    const sel = document.getElementById('autoRefreshInterval');
    sel.disabled = !sw.checked;
    if (autoRefreshTimer) {
        clearInterval(autoRefreshTimer);
        autoRefreshTimer = null;
    }
    if (sw.checked) {
        const seconds = Math.max(5, parseInt(sel.value) || 10);
        autoRefreshTimer = setInterval(() => { loadJobs(); }, seconds * 1000);
    }
}

// 页面卸载时清除自动刷新定时器
window.addEventListener('beforeunload', function() {
    if (autoRefreshTimer) {
        clearInterval(autoRefreshTimer);
        autoRefreshTimer = null;
    }
});

// 检查认证状态
async function checkAuth() {
    try {
        const response = await fetch('/api/auth/current');
        const result = await response.json();
        
        if (result.data && result.data.userId) {
            currentUser = result.data;
            document.getElementById('displayName').textContent = result.data.displayName || result.data.username;

            // Admin 标识
            if (result.data.role === 'Admin') {
                document.getElementById('roleBadge').classList.remove('d-none');
                document.getElementById('tabUserManage').classList.remove('d-none');
                document.getElementById('tabNotifyConfig').classList.remove('d-none');
                document.getElementById('menuUserManage').classList.remove('d-none');
                document.getElementById('menuUserManageDivider').classList.remove('d-none');
            }
        } else {
            window.location.href = '/login.html';
        }
    } catch (error) {
        window.location.href = '/login.html';
    }
}

// 切换 Tab
function switchTab(tab) {
    // 隐藏所有面板
    document.getElementById('panelJobs').classList.add('d-none');
    document.getElementById('panelAudit').classList.add('d-none');
    document.getElementById('panelUsers').classList.add('d-none');
    document.getElementById('panelNotify').classList.add('d-none');

    // 取消所有 tab 激活状态
    document.querySelectorAll('#mainTabs .nav-link').forEach(el => el.classList.remove('active'));

    if (tab === 'jobs') {
        document.getElementById('panelJobs').classList.remove('d-none');
        document.querySelector('#mainTabs .nav-item:nth-child(1) .nav-link').classList.add('active');
    } else if (tab === 'audit') {
        document.getElementById('panelAudit').classList.remove('d-none');
        document.querySelector('#tabAuditLog .nav-link').classList.add('active');
        loadAuditLogs();
    } else if (tab === 'users') {
        document.getElementById('panelUsers').classList.remove('d-none');
        document.querySelector('#tabUserManage .nav-link').classList.add('active');
        loadUsers();
    } else if (tab === 'notify') {
        document.getElementById('panelNotify').classList.remove('d-none');
        document.querySelector('#tabNotifyConfig .nav-link').classList.add('active');
        loadNotifyConfigs();
    }
}

// 退出登录
async function logout() {
    try {
        await fetch('/api/auth/logout', { method: 'POST' });
    } catch (error) {
        console.error('Logout error:', error);
    }
    window.location.href = '/login.html';
}

// triggerState int → displayState 中文映射
// 0=None/未知, 1=Normal/正常, 2=Paused/暂停, 3=Complete/完成, 4=Error/异常, 5=Blocked/阻塞
function mapTriggerState(triggerState) {
    const map = { 0: '未知', 1: '正常', 2: '暂停', 3: '完成', 4: '异常', 5: '阻塞' };
    return map[triggerState] ?? '未知';
}

// 加载统计数据（始终全量，不受搜索条件影响）
async function loadStats() {
    try {
        const response = await fetch('/api/job/stats');
        const result = await response.json();
        if (result.code === 0 && result.data) {
            const d = result.data;
            document.getElementById('totalJobs').textContent   = d.total   ?? 0;
            document.getElementById('runningJobs').textContent = d.normal  ?? 0;
            document.getElementById('pausedJobs').textContent  = d.paused  ?? 0;
            document.getElementById('blockedJobs').textContent = d.blocked ?? 0;
        }
    } catch (error) {
        console.error('加载统计失败:', error);
    }
}

// 标记是否已经完成过首次加载（首次显示 spinner，后续静默刷新）
let jobsLoaded = false;

// 加载任务列表（服务端分页）
async function loadJobs() {
    const tbody = document.getElementById('jobTableBody');

    if (!jobsLoaded) {
        // 首次加载：显示 spinner
        if (tbody) {
            tbody.innerHTML = `<tr><td colspan="7" class="text-center py-4">
                <div class="spinner-border text-primary" role="status">
                    <span class="visually-hidden">加载中...</span>
                </div>
            </td></tr>`;
        }
    }

    try {
        const params = new URLSearchParams({
            pageNumber: currentPage,
            pageSize:   pageSize
        });
        if (activeFilter.jobName)  params.set('jobName',  activeFilter.jobName);
        if (activeFilter.jobGroup) params.set('jobGroup', activeFilter.jobGroup);

        const response = await fetch(`/api/job/list?${params}`);
        const result = await response.json();

        if (result.code === 0 && result.data) {
            const pageData = result.data;
            const rawList  = Array.isArray(pageData.data) ? pageData.data : [];
            const jobs = rawList.map(job => ({
                ...job,
                displayState: mapTriggerState(job.triggerState)
            }));
            renderJobTable(jobs);
            renderPagination(pageData.pageInfo);
            jobsLoaded = true;
        } else {
            if (!jobsLoaded) renderJobTable([]);
            showToast(result.message || '加载失败', 'error');
        }
    } catch (error) {
        console.error('加载任务失败:', error);
        if (!jobsLoaded) renderJobTable([]);
        showToast('加载任务失败', 'error');
    }

    // 统计单独请求，不受分页影响
    loadStats();
}

// 前端过滤 + 渲染（兼容旧逻辑调用，现已改为服务端分页，直接重新加载）
function applyFilterAndRender() {
    loadJobs();
}


// 生成单行 HTML（抽离复用）
function buildJobRowHtml(job) {
    const jobKey = `${escapeHtml(job.name)}|||${escapeHtml(job.groupName)}`;
    const pauseOrResume = job.displayState === '正常'
        ? `<button class="btn btn-sm btn-outline-warning btn-action" id="btnPause_${CSS.escape(jobKey)}"
               onclick="pauseJob('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="暂停">
               <i class="bi bi-pause-fill"></i></button>`
        : `<button class="btn btn-sm btn-outline-success btn-action" id="btnResume_${CSS.escape(jobKey)}"
               onclick="resumeJob('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="恢复">
               <i class="bi bi-play-fill"></i></button>`;
    return [
        `<strong>${escapeHtml(job.name)}</strong>`,
        `<span class="badge bg-secondary">${escapeHtml(job.groupName)}</span>`,
        getStatusBadge(job.displayState),
        getTriggerBadge(job.triggerType, job.cron, job.intervalSecond),
        formatDateTime(job.previousFireTime),
        formatDateTime(job.nextFireTime),
        `<button class="btn btn-sm btn-outline-info btn-action"
             onclick="viewJobDetail('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="详情">
             <i class="bi bi-eye"></i></button>
         <button class="btn btn-sm btn-outline-secondary btn-action" id="btnTrigger_${CSS.escape(jobKey)}"
             onclick="triggerJobNow('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="立即执行">
             <i class="bi bi-lightning-fill"></i></button>
         <button class="btn btn-sm btn-outline-primary btn-action"
             onclick="viewLogs('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="日志">
             <i class="bi bi-journal-text"></i></button>
         ${pauseOrResume}
         <button class="btn btn-sm btn-outline-danger btn-action"
             onclick="deleteJob('${escapeHtml(job.name)}','${escapeHtml(job.groupName)}')" data-tip="删除">
             <i class="bi bi-trash"></i></button>`
    ];
}

// 渲染任务表格（DOM diff：只更新变化的单元格，不整体替换，消除闪烁）
function renderJobTable(jobs) {
    const tbody = document.getElementById('jobTableBody');

    // 空数据：直接替换整体（无闪烁问题）
    if (!jobs || jobs.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="7" class="text-center py-4 text-muted">
                    <i class="bi bi-inbox" style="font-size: 2rem;"></i>
                    <p class="mt-2 mb-0">暂无任务数据</p>
                </td>
            </tr>`;
        return;
    }

    // 建立现有行的 key → tr 映射（只收集带 data-job-key 的数据行）
    const existingRows = new Map();
    for (const tr of tbody.querySelectorAll('tr[data-job-key]')) {
        existingRows.set(tr.dataset.jobKey, tr);
    }

    const newKeys = new Set();
    const fragment = document.createDocumentFragment();

    for (const job of jobs) {
        const key = `${job.name}|||${job.groupName}`;
        newKeys.add(key);
        const cells = buildJobRowHtml(job);

        let tr = existingRows.get(key);
        if (!tr) {
            // 新行：创建
            tr = document.createElement('tr');
            tr.dataset.jobKey = key;
            for (let i = 0; i < cells.length; i++) {
                const td = document.createElement('td');
                td.innerHTML = cells[i];
                tr.appendChild(td);
            }
        } else {
            // 已有行：只更新内容变化的单元格
            const tds = tr.querySelectorAll('td');
            for (let i = 0; i < cells.length; i++) {
                if (tds[i] && tds[i].innerHTML !== cells[i]) {
                    tds[i].innerHTML = cells[i];
                }
            }
        }
        fragment.appendChild(tr);
    }

    // 用 replaceChildren 一次性替换 tbody 全部内容：
    // 既清除了 spinner / 空态等无 data-job-key 的行，又保证了行顺序正确
    tbody.replaceChildren(fragment);
}


// ─── 自定义轻量 Tooltip（全局事件代理，彻底解决 Bootstrap Tooltip 残留问题）───
document.addEventListener('DOMContentLoaded', function initCustomTooltip() {
    const box = document.getElementById('customTooltip');
    if (!box) return;

    let showTimer = null;

    function getTarget(e) {
        // 支持点击图标时，找到带 data-tip 的父按钮
        return e.target.closest('[data-tip]');
    }

    function show(el, e) {
        const text = el.getAttribute('data-tip');
        if (!text) return;
        box.textContent = text;
        box.classList.add('show');
        move(e);
    }

    function hide() {
        clearTimeout(showTimer);
        box.classList.remove('show');
    }

    function move(e) {
        const gap = 10;
        let x = e.clientX + gap;
        let y = e.clientY - 32;
        // 防止超出右侧
        if (x + box.offsetWidth > window.innerWidth - 4)
            x = e.clientX - box.offsetWidth - gap;
        // 防止超出顶部
        if (y < 4) y = e.clientY + gap;
        box.style.left = x + 'px';
        box.style.top  = y + 'px';
    }

    document.addEventListener('mouseover', function(e) {
        const el = getTarget(e);
        if (!el) return;
        clearTimeout(showTimer);
        showTimer = setTimeout(() => show(el, e), 120);
    });

    document.addEventListener('mouseout', function(e) {
        const el = getTarget(e);
        if (!el) return;
        // 只在真正离开该元素时隐藏（不是移到子元素）
        if (!el.contains(e.relatedTarget)) hide();
    });

    document.addEventListener('mousemove', function(e) {
        if (box.classList.contains('show')) move(e);
    });

    // 任何点击、滚动、按键都立即隐藏
    document.addEventListener('mousedown', hide);
    document.addEventListener('scroll', hide, true);
    document.addEventListener('keydown', hide);
});

// 兼容旧调用（操作函数里调用此方法确保 tooltip 隐藏）
function hideAllTooltips() {
    const box = document.getElementById('customTooltip');
    if (box) box.classList.remove('show');
}

// ── 全局 ESC 拦截：多层 modal 叠加时，只关闭最顶层的那一个 ──
// Bootstrap 5 默认 ESC 会关闭所有打开的 modal，这里覆盖该行为
(function fixNestedModalEsc() {
    // 在捕获阶段拦截 keydown，比 Bootstrap 内部监听更早执行
    document.addEventListener('keydown', function(e) {
        if (e.key !== 'Escape') return;

        // 找出所有当前显示中的 modal（有 show 类且 display 不为 none）
        const openModals = Array.from(document.querySelectorAll('.modal.show'));
        if (openModals.length === 0) return;

        // 按 z-index（Bootstrap 会动态设置）或 DOM 顺序取最后一个（最顶层）
        const topModal = openModals.reduce((top, cur) => {
            const zTop = parseInt(window.getComputedStyle(top).zIndex) || 0;
            const zCur = parseInt(window.getComputedStyle(cur).zIndex) || 0;
            return zCur >= zTop ? cur : top;
        });

        // 只关闭最顶层
        const instance = bootstrap.Modal.getInstance(topModal);
        if (instance) {
            e.stopImmediatePropagation(); // 阻止 Bootstrap 再处理这个事件
            instance.hide();
        }
    }, true); // capture: true，比 Bootstrap 的冒泡监听先触发
}());

// 弹窗加载状态：loading 时锁定整个 modal，禁止任何交互
function setModalLoading(modalId, btnId, loading) {
    const modal = document.getElementById(modalId);
    const btn = document.getElementById(btnId);
    if (!modal) return;

    if (loading) {
        // 1. 提交按钮变 loading 样式
        if (btn) {
            btn.disabled = true;
            btn.dataset.originalHtml = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status"></span> 提交中...';
        }
        // 2. 禁用所有表单元素，但排除 X 关闭按钮（.btn-close），避免弹框无法关闭
        modal.querySelectorAll('input, select, textarea, button:not(.btn-close)').forEach(el => {
            el.disabled = true;
        });
        // 3. 添加半透明遮罩到 modal-body，不覆盖 header（确保 X 按钮始终可点）
        const overlay = document.createElement('div');
        overlay.id = modalId + '_overlay';
        overlay.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;background:rgba(255,255,255,0.5);z-index:9998;cursor:not-allowed;border-radius:0 0 12px 12px;pointer-events:auto;';
        const body = modal.querySelector('.modal-body');
        if (body) {
            body.style.position = 'relative';
            body.appendChild(overlay);
        }
    } else {
        // 1. 恢复提交按钮
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = btn.dataset.originalHtml || btn.innerHTML;
        }
        // 2. 恢复所有表单元素（同样排除 btn-close，它本不该被禁用）
        modal.querySelectorAll('input, select, textarea, button:not(.btn-close)').forEach(el => {
            el.disabled = false;
        });
        // 3. 移除遮罩
        const overlay = document.getElementById(modalId + '_overlay');
        if (overlay) overlay.remove();
        // 4. readonly 字段恢复只读
        modal.querySelectorAll('[data-readonly]').forEach(el => {
            el.readOnly = true;
        });
    }
}

// 获取状态徽章
function getStatusBadge(displayState) {
    const stateMap = {
        '正常': { class: 'bg-success', text: '正常' },
        '暂停': { class: 'bg-warning text-dark', text: '暂停' },
        '完成': { class: 'bg-info', text: '完成' },
        '异常': { class: 'bg-danger', text: '异常' },
        '阻塞': { class: 'bg-dark', text: '阻塞' },
        '不存在': { class: 'bg-secondary', text: '不存在' }
    };
    const status = stateMap[displayState] || { class: 'bg-secondary', text: displayState || '未知' };
    return `<span class="badge ${status.class} badge-status">${status.text}</span>`;
}

// 获取触发方式显示内容
// triggerType: 1=Cron, 2=Simple（固定间隔）
function getTriggerBadge(triggerType, cron, intervalSecond) {
    if (triggerType === 1) {
        const expr = cron ? escapeHtml(cron) : '-';
        return `<span class="badge bg-purple text-white" style="background:#6f42c1;font-weight:500;letter-spacing:0;" title="${expr}">${expr}</span>`;
    }
    if (triggerType === 2) {
        const sec = intervalSecond != null ? intervalSecond : '-';
        return `<span class="badge bg-info text-dark" style="font-weight:500;">每 ${sec} 秒</span>`;
    }
    return '<span class="text-muted">-</span>';
}

// 格式化日期时间 => "2026-02-25 16:05:39"
function formatDateTime(dateStr) {
    if (!dateStr) return '<span class="text-muted">-</span>';
    const date = new Date(dateStr);
    if (isNaN(date.getTime())) return '<span class="text-muted">-</span>';
    const pad = n => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

// 渲染分页
function renderPagination(pageInfo) {
    if (!pageInfo) return;
    
    const pagination = document.getElementById('pagination');
    // 兼容大小写：Total/total, PageSize/pageSize
    const total = pageInfo.Total || pageInfo.total || 0;
    const pageSize = pageInfo.PageSize || pageInfo.pageSize || 20;
    const totalPages = Math.ceil(total / pageSize);
    
    console.log('Rendering pagination - Total:', total, 'PageSize:', pageSize, 'TotalPages:', totalPages, 'CurrentPage:', currentPage);
    
    let html = '';
    
    // 上一页
    html += `<li class="page-item ${currentPage === 1 ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToPage(${Math.max(1, currentPage - 1)})">上一页</a>
    </li>`;
    
    // 页码
    const startPage = Math.max(1, currentPage - 2);
    const endPage = Math.min(totalPages, currentPage + 2);
    
    for (let i = startPage; i <= endPage; i++) {
        html += `<li class="page-item ${i === currentPage ? 'active' : ''}">
            <a class="page-link" href="#" onclick="goToPage(${i})">${i}</a>
        </li>`;
    }
    
    // 下一页
    html += `<li class="page-item ${currentPage === totalPages ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToPage(${Math.min(totalPages, currentPage + 1)})">下一页</a>
    </li>`;
    
    // 分页信息
    html += `<li class="page-item disabled">
        <span class="page-link">第 ${currentPage}/${totalPages} 页 (共 ${total} 条)</span>
    </li>`;
    
    pagination.innerHTML = html;
}

// 更新统计信息（已改为由 loadStats 负责，此函数保留兼容旧调用）
function updateStats(jobs) {
    if (!jobs) return;
    document.getElementById('totalJobs').textContent   = jobs.length;
    document.getElementById('runningJobs').textContent = jobs.filter(j => j.displayState === '正常').length;
    document.getElementById('pausedJobs').textContent  = jobs.filter(j => j.displayState === '暂停').length;
    document.getElementById('blockedJobs').textContent = jobs.filter(j => j.displayState === '阻塞').length;
}

// 跳转页码（服务端分页，重新请求接口）
function goToPage(page) {
    currentPage = page;
    loadJobs();
}

// 搜索任务（用户主动点击搜索，才把输入框值写入 activeFilter）
function searchJobs() {
    activeFilter = {
        jobName:  (document.getElementById('searchJobName').value  || '').trim(),
        jobGroup: (document.getElementById('searchJobGroup').value || '').trim()
    };
    currentPage = 1;
    loadJobs();
}

// 重置搜索（清空 activeFilter 和输入框）
function resetSearch() {
    document.getElementById('searchJobName').value  = '';
    document.getElementById('searchJobGroup').value = '';
    activeFilter = { jobName: '', jobGroup: '' };
    currentPage = 1;
    loadJobs();
}

// 刷新任务列表（重新请求接口）
function refreshJobs() {
    loadJobs();
    showToast('刷新成功', 'success');
}

// 触发器类型切换
function toggleTriggerOptions() {
    const triggerType = document.getElementById('triggerType').value;
    const cronGroup = document.getElementById('cronGroup');
    const intervalGroup = document.getElementById('intervalGroup');
    
    if (triggerType === '1') {
        cronGroup.classList.remove('d-none');
        intervalGroup.classList.add('d-none');
    } else {
        cronGroup.classList.add('d-none');
        intervalGroup.classList.remove('d-none');
    }
}

// 提交新增任务
async function submitAddJob() {
    const jobName = document.getElementById('jobName').value.trim();
    const jobGroup = document.getElementById('jobGroup').value.trim();
    const triggerType = parseInt(document.getElementById('triggerType').value);
    const cron = document.getElementById('cron').value.trim();
    const intervalSecond = parseInt(document.getElementById('intervalSecond').value) || 60;
    const requestType = parseInt(document.getElementById('requestType').value);
    const requestUrl = document.getElementById('requestUrl').value.trim();
    const headers = document.getElementById('headers').value.trim();
    const requestParameters = document.getElementById('requestParameters').value.trim();
    const description = document.getElementById('description').value.trim();
    const beginTime = document.getElementById('beginTime').value;
    const endTime = document.getElementById('endTime').value;
    const runTotal = document.getElementById('runTotal').value;
    const mailNotice = parseInt(document.getElementById('mailNotice').value) || 0;
    const dingTalkNotice = parseInt(document.getElementById('dingTalkNotice').value) || 0;
    
    // 验证必填字段
    if (!jobName || !jobGroup) {
        showToast('请填写任务名称和分组', 'error');
        return;
    }
    
    if (triggerType === 1 && !cron) {
        showToast('请填写Cron表达式', 'error');
        return;
    }
    
    if (!requestUrl) {
        showToast('请填写请求URL', 'error');
        return;
    }
    
    // 验证 JSON 格式
    if (headers) {
        try {
            JSON.parse(headers);
        } catch (e) {
            showToast('Headers 格式不正确，请输入有效的 JSON', 'error');
            return;
        }
    }
    
    if (requestParameters) {
        try {
            JSON.parse(requestParameters);
        } catch (e) {
            showToast('请求参数格式不正确，请输入有效的 JSON', 'error');
            return;
        }
    }
    
    const jobData = {
        jobName,
        jobGroup,
        triggerType,
        cron: triggerType === 1 ? cron : '',
        intervalSecond: triggerType === 2 ? intervalSecond : null,
        requestType,
        requestUrl,
        headers: headers || '',
        requestParameters: requestParameters || '',
        description: description || '',
        beginTime: beginTime ? new Date(beginTime).toISOString() : new Date().toISOString(),
        endTime: endTime ? new Date(endTime).toISOString() : null,
        runTotal: runTotal ? parseInt(runTotal) : null,
        jobType: 1,
        mailMessage: mailNotice,
        dingtalk: dingTalkNotice,
        runNumber: 0
    };
    
    setModalLoading('addJobModal', 'btnSubmitAddJob', true);
    try {
        const response = await fetch('/api/job/add', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(jobData)
        });
        
        const result = await response.json();
        
        if (result.code === 0) {
            showToast('任务添加成功', 'success');
            bootstrap.Modal.getInstance(document.getElementById('addJobModal')).hide();
            document.getElementById('addJobForm').reset();
            loadJobs();
        } else {
            showToast(result.message || '添加失败', 'error');
        }
    } catch (error) {
        console.error('添加任务失败:', error);
        showToast('添加任务失败', 'error');
    } finally {
        setModalLoading('addJobModal', 'btnSubmitAddJob', false);
    }
}

// 查看任务详情（填充编辑表单）
async function viewJobDetail(jobName, jobGroup) {
    hideAllTooltips();
    currentJobName = jobName;
    currentJobGroup = jobGroup;

    // ── 先清空所有字段，避免残留上次数据 ──
    const fieldsToReset = [
        'editCron', 'editRequestUrl', 'editHeaders',
        'editRequestParameters', 'editDescription',
        'editBeginTime', 'editEndTime', 'editRunTotal'
    ];
    fieldsToReset.forEach(id => {
        const el = document.getElementById(id);
        if (el) el.value = '';
    });
    document.getElementById('editTriggerType').value = '1';
    document.getElementById('editRequestType').value = '2';
    document.getElementById('editIntervalSecond').value = '60';
    document.getElementById('editMailNotice').value = '0';
    document.getElementById('editDingTalkNotice').value = '0';
    toggleEditTriggerOptions();

    // 只读字段赋值
    document.getElementById('editJobName').value = jobName;
    document.getElementById('editJobGroup').value = jobGroup;

    // 显示 loading 遮罩
    let loadingOverlay = document.getElementById('editJobFormLoading');
    if (!loadingOverlay) {
        loadingOverlay = document.createElement('div');
        loadingOverlay.id = 'editJobFormLoading';
        loadingOverlay.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;' +
            'background:rgba(255,255,255,0.85);z-index:999;display:flex;align-items:center;' +
            'justify-content:center;border-radius:8px;';
        loadingOverlay.innerHTML = '<div class="text-center"><div class="spinner-border text-primary mb-2" role="status"></div>' +
            '<p class="text-muted small mb-0">加载中...</p></div>';
        const modalBody = document.querySelector('#jobDetailModal .modal-body');
        if (modalBody) {
            modalBody.style.position = 'relative';
            modalBody.appendChild(loadingOverlay);
        }
    } else {
        loadingOverlay.style.display = 'flex';
    }
    // 禁用保存按钮
    const saveBtn = document.getElementById('btnSubmitEditJob');
    if (saveBtn) saveBtn.disabled = true;

    const modal = new bootstrap.Modal(document.getElementById('jobDetailModal'));
    modal.show();

    try {
        const response = await fetch(`/api/job/detail?jobName=${encodeURIComponent(jobName)}&jobGroup=${encodeURIComponent(jobGroup)}`);
        const result = await response.json();

        if (result.code === 0 && result.data) {
            const job = result.data;

            // 使用 ?? 而非 || 避免 0 被误判为 falsy
            const triggerType = job.TriggerType ?? job.triggerType ?? 1;
            document.getElementById('editTriggerType').value = String(triggerType);
            toggleEditTriggerOptions();

            document.getElementById('editCron').value = job.Cron ?? job.cron ?? '';
            document.getElementById('editIntervalSecond').value = job.IntervalSecond ?? job.intervalSecond ?? 60;
            document.getElementById('editRequestType').value = String(job.RequestType ?? job.requestType ?? 2);
            document.getElementById('editRunTotal').value = (job.RunTotal ?? job.runTotal) || '';
            document.getElementById('editRequestUrl').value = job.RequestUrl ?? job.requestUrl ?? '';
            document.getElementById('editHeaders').value = job.Headers ?? job.headers ?? '';
            document.getElementById('editRequestParameters').value = job.RequestParameters ?? job.requestParameters ?? '';
            document.getElementById('editDescription').value = job.Description ?? job.description ?? '';
            document.getElementById('editMailNotice').value = String(job.MailMessage ?? job.mailMessage ?? 0);
            document.getElementById('editDingTalkNotice').value = String(job.Dingtalk ?? job.dingtalk ?? 0);

            // 时间：后端已返回 "yyyy-MM-dd HH:mm:ss" 格式，转换为 datetime-local
            const toDatetimeLocal = (val) => {
                if (!val) return '';
                return val.replace(' ', 'T').slice(0, 16);
            };
            document.getElementById('editBeginTime').value = toDatetimeLocal(job.BeginTime ?? job.beginTime);
            document.getElementById('editEndTime').value = toDatetimeLocal(job.EndTime ?? job.endTime);
        } else {
            showToast(result.message || '加载任务详情失败', 'error');
        }
    } catch (error) {
        console.error('加载任务详情异常:', error);
        showToast('加载任务详情失败', 'error');
    } finally {
        // 无论成功失败都移除 loading 遮罩、恢复保存按钮
        if (loadingOverlay) loadingOverlay.style.display = 'none';
        if (saveBtn) saveBtn.disabled = false;
    }
}

// 编辑表单触发器类型切换
function toggleEditTriggerOptions() {
    const triggerType = document.getElementById('editTriggerType').value;
    document.getElementById('editCronGroup').classList.toggle('d-none', triggerType !== '1');
    document.getElementById('editIntervalGroup').classList.toggle('d-none', triggerType === '1');
}

// 提交修改任务
async function submitEditJob() {
    const jobName = document.getElementById('editJobName').value;
    const jobGroup = document.getElementById('editJobGroup').value;
    const triggerType = parseInt(document.getElementById('editTriggerType').value);
    const cron = document.getElementById('editCron').value.trim();
    const intervalSecond = parseInt(document.getElementById('editIntervalSecond').value) || 60;
    const requestType = parseInt(document.getElementById('editRequestType').value);
    const requestUrl = document.getElementById('editRequestUrl').value.trim();
    const headers = document.getElementById('editHeaders').value.trim();
    const requestParameters = document.getElementById('editRequestParameters').value.trim();
    const description = document.getElementById('editDescription').value.trim();
    const beginTime = document.getElementById('editBeginTime').value;
    const endTime = document.getElementById('editEndTime').value;
    const runTotal = document.getElementById('editRunTotal').value;
    const mailNotice = parseInt(document.getElementById('editMailNotice').value) || 0;
    const dingTalkNotice = parseInt(document.getElementById('editDingTalkNotice').value) || 0;

    if (triggerType === 1 && !cron) {
        showToast('请填写Cron表达式', 'error');
        return;
    }
    if (!requestUrl) {
        showToast('请填写请求URL', 'error');
        return;
    }
    if (headers) {
        try { JSON.parse(headers); } catch (e) {
            showToast('Headers 格式不正确，请输入有效的 JSON', 'error');
            return;
        }
    }
    if (requestParameters) {
        try { JSON.parse(requestParameters); } catch (e) {
            showToast('请求参数格式不正确，请输入有效的 JSON', 'error');
            return;
        }
    }

    const jobData = {
        jobName,
        jobGroup,
        triggerType,
        cron: triggerType === 1 ? cron : '',
        intervalSecond: triggerType === 2 ? intervalSecond : null,
        requestType,
        requestUrl,
        headers: headers || '',
        requestParameters: requestParameters || '',
        description: description || '',
        beginTime: beginTime ? new Date(beginTime).toISOString() : new Date().toISOString(),
        endTime: endTime ? new Date(endTime).toISOString() : null,
        runTotal: runTotal ? parseInt(runTotal) : null,
        jobType: 1,
        mailMessage: mailNotice,
        dingtalk: dingTalkNotice,
        runNumber: 0
    };

    setModalLoading('jobDetailModal', 'btnSubmitEditJob', true);
    try {
        const response = await fetch('/api/job/update', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(jobData)
        });
        const result = await response.json();
        if (result.code === 0) {
            showToast('任务修改成功', 'success');
            bootstrap.Modal.getInstance(document.getElementById('jobDetailModal')).hide();
            loadJobs();
        } else {
            showToast(result.message || '修改失败', 'error');
        }
    } catch (error) {
        showToast('修改任务失败', 'error');
    } finally {
        setModalLoading('jobDetailModal', 'btnSubmitEditJob', false);
    }
}

// 获取请求类型名称
function getRequestTypeName(type) {
    const types = { 0: 'None', 1: 'GET', 2: 'POST', 4: 'PUT', 8: 'DELETE' };
    return types[type] || 'Unknown';
}

// 查看任务日志
function viewJobLogs() {
    bootstrap.Modal.getInstance(document.getElementById('jobDetailModal'))?.hide();
    viewLogs(currentJobName, currentJobGroup);
}

// 查看日志
async function viewLogs(jobName, jobGroup) {
    hideAllTooltips();
    currentJobName = jobName;
    currentJobGroup = jobGroup;
    logCurrentPage = 1;
    
    document.getElementById('logJobTitle').textContent = `${jobGroup}.${jobName}`;
    
    const modal = new bootstrap.Modal(document.getElementById('jobLogsModal'));
    modal.show();
    
    await loadLogs();
}

// 加载日志
async function loadLogs() {
    try {
        const params = new URLSearchParams({
            jobName: currentJobName,
            jobGroup: currentJobGroup,
            pageNumber: logCurrentPage,
            pageSize: logPageSize
        });
        
        const response = await fetch(`/api/job/logs?${params}`);
        const result = await response.json();
        
        // 处理 ResultFilter 包装的响应: { code: 0, message: "成功", data: { Data: [...], PageInfo: {...} } }
        if (result.code === 0 && result.data) {
            const pageResponse = result.data;
            const logs = pageResponse.Data || pageResponse.data || [];
            const pageInfo = pageResponse.PageInfo || pageResponse.pageInfo;
            
            renderLogTable(logs);
            renderLogPagination(pageInfo);
        }
    } catch (error) {
        console.error('加载日志失败:', error);
        showToast('加载日志失败', 'error');
    }
}

// 渲染日志表格
function renderLogTable(logs) {
    const tbody = document.getElementById('logTableBody');
    
    if (!logs || logs.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="7" class="text-center py-4 text-muted">
                    <i class="bi bi-journal-x" style="font-size: 2rem;"></i>
                    <p class="mt-2 mb-0">暂无执行日志</p>
                </td>
            </tr>
        `;
        return;
    }
    
    tbody.innerHTML = logs.map(log => `
        <tr>
            <td>${log.id}</td>
            <td>${formatDateTime(log.beginTime)}</td>
            <td>${formatDateTime(log.endTime)}</td>
            <td>${log.executeTime ? log.executeTime.toFixed(3) : '-'}</td>
            <td>${getExecutionStatusBadge(log.executionStatus, log.errorMsg)}</td>
            <td>${log.statusCode || '-'}</td>
            <td>
                <button class="btn btn-sm btn-outline-info" onclick="viewLogDetail(${log.id})">
                    <i class="bi bi-eye"></i> 详情
                </button>
            </td>
        </tr>
    `).join('');
    
    // 保存日志数据供详情使用
    window.logsData = logs;
}

// 获取执行状态徽章
function getExecutionStatusBadge(status, errorMsg) {
    if (errorMsg) {
        return '<span class="badge bg-danger">失败</span>';
    }
    const statusMap = {
        0: { class: 'bg-warning', text: '进行中' },
        1: { class: 'bg-success', text: '成功' },
        2: { class: 'bg-danger', text: '失败' }
    };
    const s = statusMap[status] || { class: 'bg-secondary', text: '未知' };
    return `<span class="badge ${s.class}">${s.text}</span>`;
}

// 渲染日志分页
function renderLogPagination(pageInfo) {
    if (!pageInfo) return;
    
    const pagination = document.getElementById('logPagination');
    // 兼容大小写：Total/total, PageSize/pageSize
    const total = pageInfo.Total || pageInfo.total || 0;
    const pageSize = pageInfo.PageSize || pageInfo.pageSize || 20;
    const totalPages = Math.ceil(total / pageSize);
    
    console.log('Rendering log pagination - Total:', total, 'PageSize:', pageSize, 'TotalPages:', totalPages, 'CurrentLogPage:', logCurrentPage);
    
    let html = '';
    html += `<li class="page-item ${logCurrentPage === 1 ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToLogPage(${Math.max(1, logCurrentPage - 1)})">«</a>
    </li>`;
    
    const startPage = Math.max(1, logCurrentPage - 2);
    const endPage = Math.min(totalPages, logCurrentPage + 2);
    
    for (let i = startPage; i <= endPage; i++) {
        html += `<li class="page-item ${i === logCurrentPage ? 'active' : ''}">
            <a class="page-link" href="#" onclick="goToLogPage(${i})">${i}</a>
        </li>`;
    }
    
    html += `<li class="page-item ${logCurrentPage === totalPages ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToLogPage(${Math.min(totalPages, logCurrentPage + 1)})">»</a>
    </li>`;
    
    // 分页信息
    html += `<li class="page-item disabled">
        <span class="page-link">第 ${logCurrentPage}/${totalPages} 页 (共 ${total} 条)</span>
    </li>`;
    
    pagination.innerHTML = html;
}

// 跳转日志页码
function goToLogPage(page) {
    logCurrentPage = page;
    loadLogs();
}

// 刷新日志
function refreshLogs() {
    loadLogs();
    showToast('日志已刷新', 'success');
}

// 查看日志详情
function viewLogDetail(logId) {
    const log = window.logsData?.find(l => l.id === logId);
    if (!log) return;

    // 尝试将字符串格式化为 JSON，失败则原样显示
    const formatJson = (str) => {
        if (!str) return null;
        try {
            return JSON.stringify(JSON.parse(str), null, 2);
        } catch (e) {
            return str;
        }
    };

    // 安全地把文本设置到 <pre> 元素（保留换行、防 XSS）
    const makePre = (text, extraClass = '') => {
        const pre = document.createElement('pre');
        pre.className = `p-3 rounded ${extraClass}`;
        pre.style.cssText = 'max-height:350px;overflow:auto;font-size:0.82rem;white-space:pre;word-break:break-all;';
        pre.textContent = text;
        return pre.outerHTML;
    };

    const formattedParameters = formatJson(log.parameters);
    const formattedResult = formatJson(log.result);

    const content = document.getElementById('logDetailContent');
    content.innerHTML = `
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">日志ID</label>
                <p>${log.id}</p>
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">任务名称</label>
                <p>${escapeHtml(log.jobName || '-')}</p>
            </div>
        </div>
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">开始时间</label>
                <p>${formatDateTime(log.beginTime)}</p>
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">结束时间</label>
                <p>${formatDateTime(log.endTime)}</p>
            </div>
        </div>
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">耗时</label>
                <p>${log.executeTime ? log.executeTime.toFixed(3) + ' 秒' : '-'}</p>
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">状态码</label>
                <p>${log.statusCode || '-'}</p>
            </div>
        </div>
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">请求 URL</label>
                <p class="text-break"><code>${escapeHtml(log.url || '-')}</code></p>
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label text-muted">请求类型</label>
                <p><span class="badge bg-primary">${escapeHtml(log.requestType || '-')}</span></p>
            </div>
        </div>
        ${formattedParameters ? `
        <div class="mb-3">
            <label class="form-label text-muted">请求参数</label>
            <div id="_logParamPre"></div>
        </div>` : ''}
        ${formattedResult ? `
        <div class="mb-3">
            <label class="form-label text-muted">返回结果</label>
            <div id="_logResultPre"></div>
        </div>` : ''}
        ${log.errorMsg ? `
        <div class="mb-3">
            <label class="form-label text-muted text-danger">错误信息</label>
            <div id="_logErrorPre"></div>
        </div>` : ''}
    `;

    // 用 textContent 安全赋值，保留换行
    if (formattedParameters) {
        const pre = document.createElement('pre');
        pre.className = 'bg-light p-3 rounded';
        pre.style.cssText = 'max-height:300px;overflow:auto;font-size:0.82rem;';
        pre.textContent = formattedParameters;
        document.getElementById('_logParamPre').appendChild(pre);
    }
    if (formattedResult) {
        const pre = document.createElement('pre');
        pre.className = 'bg-light p-3 rounded';
        pre.style.cssText = 'max-height:350px;overflow:auto;font-size:0.82rem;';
        pre.textContent = formattedResult;
        document.getElementById('_logResultPre').appendChild(pre);
    }
    if (log.errorMsg) {
        const pre = document.createElement('pre');
        pre.className = 'bg-danger bg-opacity-10 text-danger p-3 rounded';
        pre.style.cssText = 'max-height:200px;overflow:auto;font-size:0.82rem;';
        pre.textContent = log.errorMsg;
        document.getElementById('_logErrorPre').appendChild(pre);
    }

    const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('logDetailModal'));
    modal.show();
}

// 行内操作按钮 loading 工具（用于立即执行/暂停/恢复）
function setInlineBtnLoading(btn, loading) {
    if (!btn) return;
    if (loading) {
        btn.disabled = true;
        btn._origHtml = btn.innerHTML;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span>';
        // 隐藏 tooltip
        hideAllTooltips();
    } else {
        btn.disabled = false;
        btn.innerHTML = btn._origHtml || btn.innerHTML;
    }
}

// 立即执行任务（不影响下次调度时间）
async function triggerJobNow(jobName, jobGroup) {
    hideAllTooltips();
    if (!confirm(`确定要立即执行任务 "${jobGroup}.${jobName}" 吗？\n此操作不会影响原有调度计划。`)) return;
    const jobKey = `${jobName}|||${jobGroup}`;
    const btn = document.getElementById(`btnTrigger_${CSS.escape(jobKey)}`);
    setInlineBtnLoading(btn, true);
    try {
        const params = new URLSearchParams({ jobName, jobGroup });
        const response = await fetch(`/api/job/trigger?${params}`, { method: 'POST' });
        const result = await response.json();
        if (result.code === 0) {
            showToast('任务已触发，正在执行中...', 'success');
            // 延迟 2 秒刷新列表，等任务执行完成后 PreviousFireTime 能正确更新
            setTimeout(() => loadJobs(), 2000);
        } else {
            showToast(result.message || '触发失败', 'error');
        }
    } catch (error) {
        showToast('立即执行失败', 'error');
    } finally {
        setInlineBtnLoading(btn, false);
    }
}

// 暂停任务
async function pauseJob(jobName, jobGroup) {
    hideAllTooltips();
    if (!confirm(`确定要暂停任务 "${jobGroup}.${jobName}" 吗？`)) return;
    const jobKey = `${jobName}|||${jobGroup}`;
    const btn = document.getElementById(`btnPause_${CSS.escape(jobKey)}`);
    setInlineBtnLoading(btn, true);
    try {
        const params = new URLSearchParams({ jobName, jobGroup });
        const response = await fetch(`/api/job/pause?${params}`, { method: 'POST' });
        const result = await response.json();
        if (result.code === 0) {
            showToast('任务已暂停', 'success');
            loadJobs();
        } else {
            showToast(result.message || '暂停失败', 'error');
            setInlineBtnLoading(btn, false);
        }
    } catch (error) {
        showToast('暂停任务失败', 'error');
        setInlineBtnLoading(btn, false);
    }
}

// 恢复任务
async function resumeJob(jobName, jobGroup) {
    hideAllTooltips();
    const jobKey = `${jobName}|||${jobGroup}`;
    const btn = document.getElementById(`btnResume_${CSS.escape(jobKey)}`);
    setInlineBtnLoading(btn, true);
    try {
        const params = new URLSearchParams({ jobName, jobGroup });
        const response = await fetch(`/api/job/resume?${params}`, { method: 'POST' });
        const result = await response.json();
        if (result.code === 0) {
            showToast('任务已恢复', 'success');
            loadJobs();
        } else {
            showToast(result.message || '恢复失败', 'error');
            setInlineBtnLoading(btn, false);
        }
    } catch (error) {
        showToast('恢复任务失败', 'error');
        setInlineBtnLoading(btn, false);
    }
}

// 删除任务
async function deleteJob(jobName, jobGroup) {
    hideAllTooltips();
    if (!confirm(`确定要删除任务 "${jobGroup}.${jobName}" 吗？此操作不可恢复！`)) return;
    
    try {
        const params = new URLSearchParams({ jobName, jobGroup });
        const response = await fetch(`/api/job/delete?${params}`);
        const result = await response.json();
        
        // 处理 ResultFilter 包装的响应: { code: 0, message: "成功", data: {...} }
        if (result.code === 0) {
            showToast('任务已删除', 'success');
            loadJobs();
        } else {
            showToast(result.message || '删除失败', 'error');
        }
    } catch (error) {
        showToast('删除任务失败', 'error');
    }
}

// 修改密码
async function changePassword() {
    const oldPassword = document.getElementById('oldPassword').value;
    const newPassword = document.getElementById('newPassword').value;
    const confirmPassword = document.getElementById('confirmPassword').value;
    
    if (!oldPassword || !newPassword || !confirmPassword) {
        showToast('请填写所有密码字段', 'error');
        return;
    }
    
    if (newPassword !== confirmPassword) {
        showToast('两次输入的新密码不一致', 'error');
        return;
    }
    
    if (newPassword.length < 6) {
        showToast('新密码长度至少6位', 'error');
        return;
    }
    
    try {
        const response = await fetch('/api/auth/change-password', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ oldPassword, newPassword })
        });
        
        const result = await response.json();
        
        // API 返回格式: { code: 0, message: "成功", data: { success: true, ... } }
        if (result.code === 0 && result.data && result.data.success) {
            showToast('密码修改成功', 'success');
            bootstrap.Modal.getInstance(document.getElementById('changePasswordModal')).hide();
            document.getElementById('changePasswordForm').reset();
        } else {
            const errorMsg = result.data?.message || result.message || '密码修改失败';
            showToast(errorMsg, 'error');
        }
    } catch (error) {
        showToast('密码修改失败', 'error');
    }
}

// ============================================================
// 用户管理
// ============================================================

// 加载用户列表
// 用户列表分页状态
let userCurrentPage = 1;
const userPageSize  = 20;

async function loadUsers(page) {
    if (page !== undefined) userCurrentPage = page;
    try {
        const response = await fetch(`/api/user/list?pageNumber=${userCurrentPage}&pageSize=${userPageSize}`);
        const result = await response.json();
        if (result.code === 0 && result.data) {
            const { data, pageInfo } = result.data;
            renderUserTable(Array.isArray(data) ? data : []);
            renderUserPagination(pageInfo);
        } else {
            showToast(result.message || '加载用户失败', 'error');
        }
    } catch (error) {
        showToast('加载用户失败', 'error');
    }
}

// 渲染用户分页
function renderUserPagination(pageInfo) {
    const el = document.getElementById('userPagination');
    if (!el || !pageInfo) return;
    const total      = pageInfo.total || 0;
    const size       = pageInfo.pageSize || userPageSize;
    const totalPages = Math.ceil(total / size) || 1;

    let html = `<li class="page-item ${userCurrentPage === 1 ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="loadUsers(${userCurrentPage - 1})">上一页</a></li>`;

    const start = Math.max(1, userCurrentPage - 2);
    const end   = Math.min(totalPages, userCurrentPage + 2);
    for (let i = start; i <= end; i++) {
        html += `<li class="page-item ${i === userCurrentPage ? 'active' : ''}">
            <a class="page-link" href="#" onclick="loadUsers(${i})">${i}</a></li>`;
    }

    html += `<li class="page-item ${userCurrentPage === totalPages ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="loadUsers(${userCurrentPage + 1})">下一页</a></li>`;
    html += `<li class="page-item disabled"><span class="page-link">第 ${userCurrentPage}/${totalPages} 页（共 ${total} 条）</span></li>`;

    el.innerHTML = html;
}

// 渲染用户表格
function renderUserTable(users) {
    const tbody = document.getElementById('userTableBody');
    if (!users || users.length === 0) {
        tbody.innerHTML = `<tr><td colspan="7" class="text-center py-4 text-muted">暂无用户</td></tr>`;
        return;
    }

    tbody.innerHTML = users.map(u => {
        const isCurrentUser = currentUser && String(u.id) === String(currentUser.userId);
        const isAdminUser = u.username === 'admin';
        const canDelete = !isCurrentUser && !isAdminUser;

        return `
        <tr>
            <td><strong>${escapeHtml(u.username)}</strong></td>
            <td>${escapeHtml(u.displayName || '-')}</td>
            <td><span class="badge ${u.role === 'Admin' ? 'bg-warning text-dark' : 'bg-secondary'}">${u.role === 'Admin' ? '管理员' : '普通用户'}</span></td>
            <td><span class="badge ${u.isEnabled ? 'bg-success' : 'bg-danger'}">${u.isEnabled ? '启用' : '禁用'}</span></td>
            <td>${formatDateTime(u.createdAt)}</td>
            <td>${u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '<span class="text-muted">从未登录</span>'}</td>
            <td>
                ${canDelete
                    ? `<button class="btn btn-sm btn-outline-danger btn-action" onclick="deleteUser(${u.id}, '${escapeHtml(u.username)}')" title="删除">
                        <i class="bi bi-trash"></i> 删除
                       </button>`
                    : `<span class="text-muted small">${isCurrentUser ? '(当前用户)' : '(系统账户)'}</span>`}
            </td>
        </tr>`;
    }).join('');
}

// 提交新增用户
async function submitAddUser() {
    const username = document.getElementById('newUsername').value.trim();
    const password = document.getElementById('newUserPassword').value;
    const displayName = document.getElementById('newDisplayName').value.trim();
    const email = document.getElementById('newEmail').value.trim();
    const role = document.getElementById('newRole').value;

    if (!username || !password) {
        showToast('请填写用户名和密码', 'error');
        return;
    }
    if (password.length < 6) {
        showToast('密码至少6位', 'error');
        return;
    }

    try {
        const response = await fetch('/api/user/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password, displayName: displayName || null, email: email || null, role })
        });
        const result = await response.json();
        if (result.code === 0) {
            showToast('用户添加成功', 'success');
            bootstrap.Modal.getInstance(document.getElementById('addUserModal')).hide();
            document.getElementById('addUserForm').reset();
            loadUsers();
        } else {
            showToast(result.message || '添加失败', 'error');
        }
    } catch (error) {
        showToast('添加用户失败', 'error');
    }
}

// 删除用户
async function deleteUser(userId, username) {
    if (!confirm(`确定要删除用户 "${username}" 吗？此操作不可恢复！`)) return;

    try {
        const response = await fetch(`/api/user/${userId}`, { method: 'DELETE' });
        const result = await response.json();
        if (result.code === 0) {
            showToast('用户已删除', 'success');
            loadUsers();
        } else {
            showToast(result.message || '删除失败', 'error');
        }
    } catch (error) {
        showToast('删除用户失败', 'error');
    }
}

// ============================================================
// 操作审计日志
// ============================================================

// 搜索审计日志
function searchAuditLogs() {
    auditCurrentPage = 1;
    loadAuditLogs();
}

// 重置审计日志搜索
function resetAuditSearch() {
    document.getElementById('searchAuditOperator').value = '';
    document.getElementById('searchAuditAction').value = '';
    auditCurrentPage = 1;
    loadAuditLogs();
}

// 加载审计日志
async function loadAuditLogs() {
    const operatorName = document.getElementById('searchAuditOperator')?.value || '';
    const action = document.getElementById('searchAuditAction')?.value || '';

    try {
        const params = new URLSearchParams({
            pageNumber: auditCurrentPage,
            pageSize: auditPageSize
        });
        if (operatorName) params.append('operatorName', operatorName);
        if (action) params.append('action', action);

        const response = await fetch(`/api/audit/logs?${params}`);
        const result = await response.json();

        if (result.code === 0 && result.data) {
            const pageResponse = result.data;
            const logs = pageResponse.Data || pageResponse.data || [];
            const pageInfo = pageResponse.PageInfo || pageResponse.pageInfo;
            renderAuditTable(logs);
            renderAuditPagination(pageInfo);
        } else {
            showToast(result.message || '加载操作日志失败', 'error');
        }
    } catch (error) {
        showToast('加载操作日志失败', 'error');
    }
}

// 渲染审计日志表格
function renderAuditTable(logs) {
    window.auditLogsData = logs; // 存储供 viewRemark 使用
    const tbody = document.getElementById('auditTableBody');
    if (!logs || logs.length === 0) {
        tbody.innerHTML = `<tr><td colspan="5" class="text-center py-4 text-muted">
            <i class="bi bi-journal-x" style="font-size:2rem;"></i>
            <p class="mt-2 mb-0">暂无操作日志</p>
        </td></tr>`;
        return;
    }

    const actionColorMap = {
        '用户登录': 'bg-primary',
        '新增任务': 'bg-success',
        '删除任务': 'bg-danger',
        '暂停任务': 'bg-warning text-dark',
        '恢复任务': 'bg-info',
        '立即执行': 'bg-warning text-dark',
        '新增用户': 'bg-primary',
        '删除用户': 'bg-danger',
        '新增推送配置': 'bg-success',
        '修改推送配置': 'bg-info',
        '删除推送配置': 'bg-danger',
        '测试推送配置': 'bg-secondary',
    };

    tbody.innerHTML = logs.map(log => {
        const badgeClass = actionColorMap[log.action] || 'bg-secondary';
        // 备注列：有内容时显示可点击链接，否则显示 -
        const remarkCell = log.remark
            ? `<a href="#" class="text-primary text-decoration-none" onclick="viewRemark(${log.id}); return false;">
                <i class="bi bi-file-code"></i> 查看详情
               </a>`
            : '<span class="text-muted">-</span>';
        return `
        <tr>
            <td>${formatDateTime(log.createdAt)}</td>
            <td>${escapeHtml(log.operatorDisplayName || log.operator)}${log.operatorDisplayName && log.operatorDisplayName !== log.operator ? ` <small class="text-muted">(${escapeHtml(log.operator)})</small>` : ''}</td>
            <td><span class="badge ${badgeClass}">${escapeHtml(log.action)}</span></td>
            <td>${escapeHtml(log.target || '-')}</td>
            <td>${remarkCell}</td>
        </tr>`;
    }).join('');
}

// 查看操作日志备注详情
function viewRemark(logId) {
    const log = window.auditLogsData?.find(l => l.id === logId);
    if (!log || !log.remark) return;

    // 尝试格式化 JSON，否则直接显示原文
    let content = log.remark;
    try {
        content = JSON.stringify(JSON.parse(log.remark), null, 2);
    } catch (e) { /* 非 JSON，原样显示 */ }

    // 用 textContent 赋值保留换行
    const pre = document.getElementById('remarkJsonContent');
    pre.textContent = content;

    new bootstrap.Modal(document.getElementById('remarkDetailModal')).show();
}

// 渲染审计日志分页
function renderAuditPagination(pageInfo) {
    if (!pageInfo) return;
    const pagination = document.getElementById('auditPagination');
    const total = pageInfo.Total || pageInfo.total || 0;
    const ps = pageInfo.PageSize || pageInfo.pageSize || auditPageSize;
    const totalPages = Math.ceil(total / ps);

    let html = '';
    html += `<li class="page-item ${auditCurrentPage === 1 ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToAuditPage(${Math.max(1, auditCurrentPage - 1)})">上一页</a>
    </li>`;
    const startPage = Math.max(1, auditCurrentPage - 2);
    const endPage = Math.min(totalPages, auditCurrentPage + 2);
    for (let i = startPage; i <= endPage; i++) {
        html += `<li class="page-item ${i === auditCurrentPage ? 'active' : ''}">
            <a class="page-link" href="#" onclick="goToAuditPage(${i})">${i}</a>
        </li>`;
    }
    html += `<li class="page-item ${auditCurrentPage >= totalPages ? 'disabled' : ''}">
        <a class="page-link" href="#" onclick="goToAuditPage(${Math.min(totalPages, auditCurrentPage + 1)})">下一页</a>
    </li>`;
    html += `<li class="page-item disabled">
        <span class="page-link">第 ${auditCurrentPage}/${totalPages || 1} 页 (共 ${total} 条)</span>
    </li>`;
    pagination.innerHTML = html;
}

function goToAuditPage(page) {
    auditCurrentPage = page;
    loadAuditLogs();
}

// HTML 转义
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 显示提示消息
// ==================== 推送配置 ====================

// 缓存：channel -> 数据库记录
let notifyDataMap = {};

// 切换左侧渠道 Tab
function switchNotifyTab(channel) {
    // 更新 tab 激活样式
    ['DingTalk', 'Email', 'Feishu'].forEach(ch => {
        const tab = document.getElementById(`notifyTab_${ch}`);
        if (tab) tab.classList.toggle('active', ch === channel);
    });
    // 切换右侧表单显示
    document.querySelectorAll('.notify-channel-form').forEach(f => f.classList.add('d-none'));
    const form = document.getElementById(`notifyForm_${channel}`);
    if (form) form.classList.remove('d-none');
    // 记录当前渠道
    document.getElementById('notifyCurrentChannel').value = channel;
    // 填数据
    if (channel === 'DingTalk') fillNotifyForm('DingTalk');
    if (channel === 'Email')    fillNotifyForm('Email');
}

// 加载推送配置列表
async function loadNotifyConfigs() {
    const loadingEl = document.getElementById('notifyFormLoading');
    const areaEl = document.getElementById('notifyFormArea');
    if (loadingEl) loadingEl.classList.remove('d-none');
    if (areaEl) areaEl.classList.add('d-none');
    try {
        const response = await fetch('/api/notify/list');
        const result = await response.json();
        if (result.code === 0) {
            notifyDataMap = {};
            (result.data || []).forEach(c => {
                const ch = c.Channel ?? c.channel;
                if (ch) notifyDataMap[ch] = c;
            });
        }
    } catch (e) { /* 忽略 */ }
    if (loadingEl) loadingEl.classList.add('d-none');
    if (areaEl) areaEl.classList.remove('d-none');
    // 默认显示钉钉
    switchNotifyTab('DingTalk');
}

// 将渠道数据填入表单
function fillNotifyForm(channel) {
    if (channel === 'DingTalk') {
        const c = notifyDataMap['DingTalk'];
        let cfg = {};
        try { cfg = JSON.parse(c?.Config ?? c?.config ?? '{}'); } catch (e) {}
        document.getElementById('dt_webhookUrl').value = cfg.webhookUrl ?? '';
        document.getElementById('dt_secret').value     = cfg.secret    ?? '';
        return;
    }
    if (channel === 'Email') {
        const c = notifyDataMap['Email'];
        let cfg = {};
        try { cfg = JSON.parse(c?.Config ?? c?.config ?? '{}'); } catch (e) {}
        document.getElementById('email_smtpHost').value  = cfg.smtpHost  ?? '';
        document.getElementById('email_smtpPort').value  = cfg.smtpPort  ?? 465;
        document.getElementById('email_useSsl').checked  = cfg.useSsl    !== false;
        document.getElementById('email_username').value  = cfg.username  ?? '';
        document.getElementById('email_password').value  = cfg.password  ?? '';
        document.getElementById('email_fromName').value  = cfg.fromName  ?? '';
        document.getElementById('email_to').value        = cfg.to        ?? '';
    }
}

// 保存（钉钉 / 邮件）
async function saveNotifyInline() {
    const channel = document.getElementById('notifyCurrentChannel').value;

    // ── 飞书：暂未实现 ──
    if (channel === 'Feishu') { showToast('飞书推送暂未实现', 'error'); return; }

    let name, config;

    if (channel === 'DingTalk') {
        const webhookUrl = document.getElementById('dt_webhookUrl').value.trim();
        const secret     = document.getElementById('dt_secret').value.trim();
        if (!webhookUrl) { showToast('请填写 Webhook 连接', 'error'); return; }
        name   = '钉钉机器人';
        config = JSON.stringify({ webhookUrl, secret });
    } else if (channel === 'Email') {
        const smtpHost = document.getElementById('email_smtpHost').value.trim();
        const smtpPort = parseInt(document.getElementById('email_smtpPort').value) || 465;
        const useSsl   = document.getElementById('email_useSsl').checked;
        const username = document.getElementById('email_username').value.trim();
        const password = document.getElementById('email_password').value.trim();
        const fromName = document.getElementById('email_fromName').value.trim();
        const to       = document.getElementById('email_to').value.trim();
        if (!smtpHost)  { showToast('请填写 SMTP 服务器地址', 'error'); return; }
        if (!username)  { showToast('请填写发件人账号', 'error'); return; }
        if (!password)  { showToast('请填写授权码/密码', 'error'); return; }
        if (!to)        { showToast('请填写收件人地址', 'error'); return; }
        name   = '邮件通知';
        config = JSON.stringify({ smtpHost, smtpPort, useSsl, username, password, fromName, to });
    } else {
        showToast('未知渠道类型', 'error'); return;
    }

    // 直接从 notifyDataMap 读当前渠道的 id，避免共享 hidden input 互相覆盖
    const existing = notifyDataMap[channel];
    const id = existing ? (existing.Id ?? existing.id ?? '') : '';

    try {
        const res = await fetch(id ? `/api/notify/${id}` : '/api/notify/create', {
            method: id ? 'PUT' : 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, channel, config, isEnabled: true })
        });
        const result = await res.json();
        if (result.code === 0) {
            showToast('保存成功', 'success');
            await loadNotifyConfigs();
            // 保存后切换回当前渠道（loadNotifyConfigs 会默认跳到 DingTalk）
            switchNotifyTab(channel);
        } else { showToast(result.message || '保存失败', 'error'); }
    } catch (e) { showToast('保存失败', 'error'); }
}

// 测试（钉钉 / 邮件，飞书暂未实现）
async function testNotifyInline() {
    const channel = document.getElementById('notifyCurrentChannel').value;
    if (channel === 'Feishu') { showToast('飞书推送暂未实现', 'error'); return; }

    // 直接从 notifyDataMap 读当前渠道的 id
    const existing = notifyDataMap[channel];
    const id = existing ? (existing.Id ?? existing.id ?? '') : '';
    if (!id) { showToast('请先保存配置再测试', 'error'); return; }

    const label = channel === 'Email' ? '邮件' : '钉钉';
    showToast(`正在发送${label}测试消息...`, 'info');
    try {
        const res = await fetch(`/api/notify/${id}/test`, { method: 'POST' });
        const result = await res.json();
        if (result.code === 0) showToast(`${label}测试消息已发送，请查收`, 'success');
        else showToast(result.message || '测试失败', 'error');
    } catch (e) { showToast('测试失败', 'error'); }
}

// 兼容旧调用占位
function renderNotifyTable() {}
function toggleNotifyEnabled() {}
function openEditNotify() {}
async function submitAddNotify() {}
async function submitEditNotify() {}
async function deleteNotify() {}
async function testNotify() {}

function showToast(message, type = 'info') {
    let toastContainer = document.getElementById('toastContainer');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toastContainer';
        toastContainer.className = 'toast-container position-fixed top-0 end-0 p-3';
        toastContainer.style.zIndex = '9999';
        document.body.appendChild(toastContainer);
    }
    
    const bgClass = type === 'success' ? 'bg-success' : type === 'error' ? 'bg-danger' : 'bg-info';
    const icon = type === 'success' ? 'check-circle' : type === 'error' ? 'x-circle' : 'info-circle';
    
    const toastId = 'toast_' + Date.now();
    const toastHtml = `
        <div id="${toastId}" class="toast align-items-center text-white ${bgClass} border-0" role="alert">
            <div class="d-flex">
                <div class="toast-body">
                    <i class="bi bi-${icon}"></i> ${escapeHtml(message)}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        </div>
    `;
    
    toastContainer.insertAdjacentHTML('beforeend', toastHtml);
    
    const toastElement = document.getElementById(toastId);
    const toast = new bootstrap.Toast(toastElement, { delay: 3000 });
    toast.show();
    
    toastElement.addEventListener('hidden.bs.toast', () => {
        toastElement.remove();
    });
}
