/**
 * i18n.js — Shared Internationalization Module
 * Supports: zh (Chinese), en (English)
 * Usage: include this script, then call window.I18n.useI18n(Vue) in each page.
 */
(function (global) {
    'use strict';

    const STORAGE_KEY = 'SL_LANG';

    // ─────────────────────────────────────────────
    // Translation Dictionaries
    // ─────────────────────────────────────────────
    const messages = {
        zh: {
            // ── Common / Header ──
            langToggle: 'EN',
            pageJump: '[ 页面跳转 ]',
            pageLatencyMatrix: '延迟矩阵',
            pageEdgeNode: '边缘节点',
            pageRawData: '原始数据',
            pageAdminPanel: '管理面板',
            nodeSyncLabel: '节点同步:',
            authOk: '🔑 已认证',
            authAdd: '🔒 添加密钥',

            // ── index.html ──
            headerSubtitle: '// 全网节点延迟监控',
            topPerformers: '🏆 优秀节点',
            healthScore: '健康评分',
            criticalAlerts: '⚠️ 关键告警',
            lowestHealth: '最低健康度',
            allNominal: '所有系统正常',
            monitoringMetrics: '▶ 监控指标',
            tabLatency: '延迟 (ms)',
            tabLoss1m: '丢包 1m',
            tabLoss10m: '丢包 10m',
            tabLoss30m: '丢包 30m',
            activeNodes: '活跃节点:',
            colSource: '来源',
            colTarget: '目标',
            waitingSync: '等待活跃节点同步...',
            inboundScore: '入站评分',
            outboundScore: '出站评分',
            avgOutboundLatency: '平均出站延迟 (5m)',
            modalClose: '关闭',
            authTitle: '身份认证',
            authPlaceholder: '输入访问密钥',
            authSave: '保存并连接',
            authClear: '清除密钥',

            // ── node.html ──
            nodeHeaderSubtitle: '// 单点拨测控制台',
            nodeNameLabel: '$ 节点名称',
            nodeIpLabel: '$ 节点 IP',
            nodeIpHint: '(覆盖)',
            accessKeyLabel: '$ 访问密钥',
            nodeIpPlaceholder: '自动检测 IP',
            accessKeyPlaceholder: '输入密钥',
            btnStartNode: '启动节点',
            btnDisconnect: '断开连接',
            logsLabel: '// 系统日志',
            logsAwaiting: '// 等待连接...',
            statusOnline: '在线',
            statusOffline: '离线',

            // ── data.html ──
            dataHeaderSubtitle: '// 实时数据流日志',
            matrixApiLabel: '// 矩阵接口',
            loadingData: '// 加载数据流...',
            btnLogout: '退出登录',

            // ── admin.html ──
            adminHeaderSubtitle: '// 系统管理中心',
            authTitle2: '身份认证',
            authKeyPlaceholder: '输入访问密钥',
            btnLogin: '登录',
            errAccessDenied: 'ACCESS_DENIED',
            errConnectionError: 'CONNECTION_ERROR',
            dashboardTitle: '控制面板',
            btnLogoutAdmin: '退出',
            installGuideTitle: 'INSTALLATION_GUIDE',
            installGuideDesc: '// 为新节点或服务器生成一键安装命令',
            modeLabel: '$ 模式',
            modeClient: 'Client（边缘节点）',
            modeServer: 'Server（控制平面）',
            modeHint: '边缘节点上报延迟；控制平面汇总数据',
            nodeNameAdminLabel: '$ 节点名称',
            nodeNameHint: '节点在矩阵中的标识符',
            serverUrlLabel: '$ 服务器地址',
            serverUrlHint: '控制平面 API 地址',
            nodeIpOverrideLabel: '$ 节点 IP',
            nodeIpOverrideHint: '覆盖向对端上报的 IP 地址',
            bindPortLabel: '$ 绑定端口',
            bindPortHint: '监听 Client 请求的 TCP 端口',
            publicIpApiLabel: '$ 公网IP接口',
            publicIpApiHint: '自定义获取服务器公网 IP 的接口',
            accessKeyAdminLabel: '$ 访问密钥',
            accessKeyAdminHint: '必须与控制平面配置一致',
            generatedCmdLabel: '// 生成命令',
            cmdNote: '* 假设 ServerLatency 二进制文件在当前目录。请以 root 权限运行。',
            btnCopy: '复制',
            nodeMgmtTitle: 'NODE_MANAGEMENT',
            noNodesFound: '// 未发现在线节点',
            lastSeen: '最后活跃:',
            checkStatusLabel: '// 检查状态',
            updateConfigLabel: '// 更新配置并重启',
            uninstallLabel: '// 卸载服务',
        },

        en: {
            // ── Common / Header ──
            langToggle: '中',
            pageJump: '[ PAGE_JUMP ]',
            pageLatencyMatrix: 'LATENCY_MATRIX',
            pageEdgeNode: 'EDGE_NODE',
            pageRawData: 'RAW_DATA',
            pageAdminPanel: 'ADMIN_PANEL',
            nodeSyncLabel: 'NODE_SYNC:',
            authOk: '🔑 AUTH_OK',
            authAdd: '🔒 ADD_AUTH',

            // ── index.html ──
            headerSubtitle: '// Global Node Latency Monitor',
            topPerformers: '🏆 Top Performers',
            healthScore: 'Health Score',
            criticalAlerts: '⚠️ Critical Alerts',
            lowestHealth: 'Lowest Health',
            allNominal: 'All systems nominal',
            monitoringMetrics: '▶ MONITORING METRICS',
            tabLatency: 'LATENCY (ms)',
            tabLoss1m: 'LOSS 1m',
            tabLoss10m: 'LOSS 10m',
            tabLoss30m: 'LOSS 30m',
            activeNodes: 'ACTIVE NODES:',
            colSource: 'SOURCE',
            colTarget: 'TARGET',
            waitingSync: 'Waiting for active nodes synchronization...',
            inboundScore: 'Inbound Score',
            outboundScore: 'Outbound Score',
            avgOutboundLatency: 'Avg Outbound Latency (5m)',
            modalClose: 'Close',
            authTitle: 'AUTHENTICATION',
            authPlaceholder: 'Enter Access Key',
            authSave: 'SAVE & CONNECT',
            authClear: 'CLEAR AUTH',

            // ── node.html ──
            nodeHeaderSubtitle: '// Single-Node Probe Console',
            nodeNameLabel: '$ NODE_NAME',
            nodeIpLabel: '$ NODE_IP',
            nodeIpHint: '(Override)',
            accessKeyLabel: '$ ACCESS_KEY',
            nodeIpPlaceholder: 'Auto-detected IP',
            accessKeyPlaceholder: 'Enter Secret Key',
            btnStartNode: 'START_NODE',
            btnDisconnect: 'DISCONNECT',
            logsLabel: '// SYSTEM_LOGS',
            logsAwaiting: '// Awaiting connection...',
            statusOnline: 'ONLINE',
            statusOffline: 'OFFLINE',

            // ── data.html ──
            dataHeaderSubtitle: '// Real-time Data Stream Log',
            matrixApiLabel: '// MATRIX_API',
            loadingData: '// Loading data stream...',
            btnLogout: 'LOGOUT',

            // ── admin.html ──
            adminHeaderSubtitle: '// System Administration',
            authTitle2: 'AUTHENTICATION',
            authKeyPlaceholder: 'Enter Access Key',
            btnLogin: 'LOGIN',
            errAccessDenied: 'ACCESS_DENIED',
            errConnectionError: 'CONNECTION_ERROR',
            dashboardTitle: 'DASHBOARD',
            btnLogoutAdmin: 'LOGOUT',
            installGuideTitle: 'INSTALLATION_GUIDE',
            installGuideDesc: '// Generate one-click install commands for new nodes or servers',
            modeLabel: '$ MODE',
            modeClient: 'Client (Edge Node)',
            modeServer: 'Server (Control Plane)',
            modeHint: 'Edge Node reports latency; Control Plane aggregates it',
            nodeNameAdminLabel: '$ NODE_NAME',
            nodeNameHint: 'Identifier for the node in the matrix',
            serverUrlLabel: '$ SERVER_URL',
            serverUrlHint: 'Control Plane API address',
            nodeIpOverrideLabel: '$ NODE_IP (Override)',
            nodeIpOverrideHint: 'Override the IP address reported to peers',
            bindPortLabel: '$ BIND_PORT',
            bindPortHint: 'TCP port to listen for incoming Client requests',
            publicIpApiLabel: '$ PUBLIC_IP_API (Optional)',
            publicIpApiHint: 'Custom API endpoint to fetch server\'s public IP',
            accessKeyAdminLabel: '$ ACCESS_KEY',
            accessKeyAdminHint: 'Must match the Control Plane configuration',
            generatedCmdLabel: '// GENERATED_COMMAND',
            cmdNote: '* Assumes ServerLatency binary is in current directory. Run with root privileges.',
            btnCopy: 'COPY',
            nodeMgmtTitle: 'NODE_MANAGEMENT',
            noNodesFound: '// NO_ONLINE_NODES_FOUND',
            lastSeen: 'Last Seen:',
            checkStatusLabel: '// CHECK_STATUS',
            updateConfigLabel: '// UPDATE_CONFIG & RESTART',
            uninstallLabel: '// UNINSTALL_SERVICE',
        }
    };

    // ─────────────────────────────────────────────
    // Core helpers
    // ─────────────────────────────────────────────
    function getSavedLocale() {
        return localStorage.getItem(STORAGE_KEY) || 'zh';
    }

    function saveLocale(lang) {
        localStorage.setItem(STORAGE_KEY, lang);
    }

    /**
     * Vue composable — call inside setup():
     *   const { t, locale, toggleLang } = useI18n();
     */
    function useI18n(Vue) {
        const { ref, computed } = Vue;
        const locale = ref(getSavedLocale());

        const t = computed(() => {
            return (key) => (messages[locale.value] || messages['zh'])[key] || key;
        });

        const toggleLang = () => {
            locale.value = locale.value === 'zh' ? 'en' : 'zh';
            saveLocale(locale.value);
        };

        return { locale, t, toggleLang };
    }

    // ─────────────────────────────────────────────
    // Expose globally
    // ─────────────────────────────────────────────
    global.I18n = { useI18n, getSavedLocale, messages };

})(window);
