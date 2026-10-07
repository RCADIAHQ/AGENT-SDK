// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK v2: WebGL JavaScript bridge
//  https://rcadia.xyz/docs/agent-sdk
//
//  Unity WebGL plugin that turns the C# DllImport calls into postMessage
//  calls to the RCADIA host page, and inbound messages into SendMessage
//  calls on the "RcadiaAgentReceiver" GameObject. Mirrors rcadia-agent.js v2.
//  Protocol: https://rcadia.xyz/sdk/AGENT_PROTOCOL_V2.md
//
//  Outbound (game to platform):
//    AGENT_CONFIGURE, AGENT_STATE, AGENT_OBSERVATION, AGENT_LEGAL_ACTIONS,
//    AGENT_ACTION_REJECTED (carries the actionId of the player's last action),
//    AGENT_GAME_OVER, AGENT_REMATCH
//
//  Inbound (platform to game), accepted only from window.parent:
//    AGENT_SESSION_START  -> OnSessionStart  {"sessionId","round","players"}
//    AGENT_GAME_START     -> OnGameStart     {"sessionId","round","players"}
//    AGENT_ACTION         -> OnAgentAction   {"playerId","actionId","actionJson"}
//                            (repeats of an actionId are dropped)
//    AGENT_TIMEOUT        -> OnTimeout       {"playerId","strike","maxStrikes","final"}
//    AGENT_SESSION_END    -> OnSessionEnd    {"sessionId","resultJson","reason"}
//    AGENT_ACTION_REJECTED-> OnActionRejected "playerId|reason" (v1; never sent)
//
//  Older backends: an AGENT_ACTION from "__system__" with type "__game_start__"
//  goes to OnGameStart, and an action with type "__timeout__" goes to OnTimeout.
// ═══════════════════════════════════════════════════════════════════════

var RcadiaAgentBridgeLib = {

    // ─── Shared state (plain data only) ──────────────────────────────

    $RcadiaAgentState: {
        initialized: false,
        sessionId: null,
        round: 0,
        players: [],
        gameObjectName: 'RcadiaAgentReceiver',
        seenActionIds: {},
        seenOrder: [],
        lastActionId: {},
        config: null
    },

    // ─── Helpers ─────────────────────────────────────────────────────

    $RcadiaAgentPost: function (type, data) {
        if (window.parent && window.parent !== window) {
            window.parent.postMessage({ type: type, data: data }, '*');
        } else {
            console.log('[RcadiaAgent] ' + type + ':', data);
        }
    },

    $RcadiaAgentInit__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    $RcadiaAgentInit: function () {
        var S = RcadiaAgentState;
        if (S.initialized) return;
        S.initialized = true;

        function send(method, payload) {
            var instance = window.unityInstance;
            if (instance && typeof instance.SendMessage === 'function') {
                instance.SendMessage(S.gameObjectName, method, payload);
            } else if (typeof Module !== 'undefined' && typeof Module.SendMessage === 'function') {
                Module.SendMessage(S.gameObjectName, method, payload);
            } else {
                console.warn('[RcadiaAgent] window.unityInstance is not set; dropped ' + method);
            }
        }

        function remember(actionId) {
            S.seenActionIds[actionId] = true;
            S.seenOrder.push(actionId);
            if (S.seenOrder.length > 500) {
                delete S.seenActionIds[S.seenOrder.shift()];
            }
        }

        function startGame(d) {
            if (d.sessionId) S.sessionId = d.sessionId;
            S.players = Array.isArray(d.players) ? d.players.slice() : [];
            S.round = typeof d.round === 'number' ? d.round : (S.round || 1);
            S.lastActionId = {};
            send('OnGameStart', JSON.stringify({
                sessionId: S.sessionId || '',
                round: S.round,
                players: S.players
            }));
        }

        function sendTimeout(playerId, strike, maxStrikes, isFinal) {
            send('OnTimeout', JSON.stringify({
                playerId: playerId,
                strike: strike,
                maxStrikes: maxStrikes,
                final: isFinal
            }));
        }

        window.addEventListener('message', function (event) {
            if (window.parent === window) return;
            if (event.source !== window.parent) return;   // only the RCADIA host page talks to the game
            var msg = event.data;
            if (!msg || typeof msg.type !== 'string') return;
            var d = msg.data || {};

            switch (msg.type) {
                case 'AGENT_REQUEST_CONFIGURE':
                    // The host started listening after we loaded, or lost its session.
                    if (S.config) RcadiaAgentPost('AGENT_CONFIGURE', S.config);
                    return;

                case 'AGENT_SESSION_START':
                    if (d.sessionId) S.sessionId = d.sessionId;
                    if (typeof d.round === 'number') S.round = d.round;
                    if (Array.isArray(d.players)) S.players = d.players.slice();
                    send('OnSessionStart', JSON.stringify({
                        sessionId: S.sessionId || '',
                        round: S.round || 0,
                        players: S.players
                    }));
                    return;

                case 'AGENT_GAME_START':
                    startGame(d);
                    return;

                case 'AGENT_ACTION': {
                    var action = d.action;
                    if (d.playerId === '__system__' && action && action.type === '__game_start__') {
                        // Older backends announce the game start as a system action.
                        startGame({ sessionId: S.sessionId, players: action.players, round: S.round || 1 });
                        return;
                    }
                    if (action && action.type === '__timeout__') {
                        sendTimeout(d.playerId, 1, -1, false);
                        return;
                    }
                    if (d.actionId) {
                        if (S.seenActionIds[d.actionId]) return;   // redelivery of an action already handled
                        remember(d.actionId);
                        S.lastActionId[d.playerId] = d.actionId;
                    }
                    send('OnAgentAction', JSON.stringify({
                        playerId: d.playerId,
                        actionId: d.actionId || '',
                        actionJson: JSON.stringify(action === undefined ? null : action)
                    }));
                    return;
                }

                case 'AGENT_TIMEOUT':
                    if (!d.playerId) return;
                    sendTimeout(
                        d.playerId,
                        typeof d.strike === 'number' ? d.strike : 1,
                        typeof d.maxStrikes === 'number' ? d.maxStrikes : -1,
                        !!d.final
                    );
                    return;

                case 'AGENT_SESSION_END':
                    send('OnSessionEnd', JSON.stringify({
                        sessionId: d.sessionId || S.sessionId || '',
                        resultJson: JSON.stringify(d.result !== undefined ? d.result : null),
                        reason: d.reason || ''
                    }));
                    return;

                case 'AGENT_ACTION_REJECTED':
                    send('OnActionRejected', d.playerId + '|' + (d.reason || 'Unknown reason'));
                    return;
            }
        });
    },

    // ─── Outbound: game to platform ──────────────────────────────────

    RcadiaAgent_Configure__deps: ['$RcadiaAgentState', '$RcadiaAgentInit', '$RcadiaAgentPost'],
    RcadiaAgent_Configure: function (configJsonPtr) {
        RcadiaAgentInit();
        var json = UTF8ToString(configJsonPtr);
        var config;
        try { config = JSON.parse(json); } catch (e) { config = {}; }

        var cfg = {
            minPlayers: config.minPlayers || 1,
            maxPlayers: config.maxPlayers || 2,
            turnBased: config.turnBased !== false,
            simultaneous: !!config.simultaneous,
            gameType: config.gameType || ''
        };
        if (config.sdkVersion) cfg.sdkVersion = config.sdkVersion;
        // -1 means "use the platform default", so leave the field out. 0 (no clock) is kept.
        if (typeof config.turnTimeout === 'number' && config.turnTimeout >= 0) cfg.turnTimeout = config.turnTimeout;
        if (typeof config.maxTurnTimeouts === 'number' && config.maxTurnTimeouts >= 0) cfg.maxTurnTimeouts = config.maxTurnTimeouts;
        if (typeof config.forfeitOnTimeout === 'boolean') cfg.forfeitOnTimeout = config.forfeitOnTimeout;
        if (typeof config.autoRematch === 'boolean') cfg.autoRematch = config.autoRematch;

        RcadiaAgentState.config = cfg;
        RcadiaAgentPost('AGENT_CONFIGURE', cfg);
    },

    RcadiaAgent_SetState__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_SetState: function (stateJsonPtr) {
        var json = UTF8ToString(stateJsonPtr);
        var state;
        try { state = JSON.parse(json); } catch (e) { state = json; }
        RcadiaAgentPost('AGENT_STATE', {
            sessionId: RcadiaAgentState.sessionId,
            state: state,
            timestamp: Date.now()
        });
    },

    RcadiaAgent_SetObservation__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_SetObservation: function (playerIdPtr, observationJsonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var json = UTF8ToString(observationJsonPtr);
        var observation;
        try { observation = JSON.parse(json); } catch (e) { observation = json; }
        RcadiaAgentPost('AGENT_OBSERVATION', {
            sessionId: RcadiaAgentState.sessionId,
            playerId: playerId,
            observation: observation,
            timestamp: Date.now()
        });
    },

    RcadiaAgent_SetLegalActions__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_SetLegalActions: function (playerIdPtr, actionsJsonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var json = UTF8ToString(actionsJsonPtr);
        var actions;
        try { actions = JSON.parse(json); } catch (e) { actions = []; }
        RcadiaAgentPost('AGENT_LEGAL_ACTIONS', {
            sessionId: RcadiaAgentState.sessionId,
            playerId: playerId,
            actions: actions,
            timestamp: Date.now()
        });
    },

    RcadiaAgent_RejectAction__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_RejectAction: function (playerIdPtr, reasonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var reason = UTF8ToString(reasonPtr);
        RcadiaAgentPost('AGENT_ACTION_REJECTED', {
            sessionId: RcadiaAgentState.sessionId,
            playerId: playerId,
            reason: reason,
            actionId: RcadiaAgentState.lastActionId[playerId] || null,
            timestamp: Date.now()
        });
    },

    RcadiaAgent_EndGame__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_EndGame: function (resultJsonPtr) {
        var json = UTF8ToString(resultJsonPtr);
        var result;
        try { result = JSON.parse(json); } catch (e) { result = {}; }
        RcadiaAgentPost('AGENT_GAME_OVER', {
            sessionId: RcadiaAgentState.sessionId,
            result: result,
            timestamp: Date.now()
        });
    },

    RcadiaAgent_Rematch__deps: ['$RcadiaAgentState', '$RcadiaAgentPost'],
    RcadiaAgent_Rematch: function () {
        RcadiaAgentPost('AGENT_REMATCH', {
            sessionId: RcadiaAgentState.sessionId,
            timestamp: Date.now()
        });
    }
};

mergeInto(LibraryManager.library, RcadiaAgentBridgeLib);
