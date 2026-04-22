// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK — WebGL JavaScript Bridge
//  https://rcadia.xyz
//
//  Unity WebGL plugin that bridges C# DllImport calls to postMessage.
//  Communicates with the RCADIA platform (parent iframe host).
//
//  Outbound (game → platform):
//    AGENT_CONFIGURE        — game declares agent support
//    AGENT_STATE            — game pushes authoritative state
//    AGENT_OBSERVATION      — game pushes per-player view (hidden info)
//    AGENT_LEGAL_ACTIONS    — game declares legal actions for a player
//    AGENT_ACTION_REJECTED  — game rejects an invalid action
//    AGENT_GAME_OVER        — game reports result
//
//  Inbound (platform → game):
//    AGENT_ACTION           — agent submits an action
//    AGENT_SESSION_START    — platform assigns session ID + player list
//    AGENT_ACTION_REJECTED  — platform relays rejection back to agent
//    AGENT_TIMEOUT          — platform signals a player timed out
// ═══════════════════════════════════════════════════════════════════════

var RcadiaAgentBridgeLib = {

    // ─── Setup ───────────────────────────────────────────────────────

    $RcadiaAgentState: {
        initialized: false,
        sessionId: null,
        gameObjectName: 'RcadiaAgentReceiver'
    },

    RcadiaAgent_Init: function () {
        if (RcadiaAgentState.initialized) return;
        RcadiaAgentState.initialized = true;

        // Listen for inbound messages from the RCADIA platform
        window.addEventListener('message', function (event) {
            if (!event.data || !event.data.type) return;

            // Platform sends an action from an agent or human player
            if (event.data.type === 'AGENT_ACTION') {
                var d = event.data.data;
                if (d && window.unityInstance) {
                    var payload = d.playerId + '|' + JSON.stringify(d.action);
                    window.unityInstance.SendMessage(
                        RcadiaAgentState.gameObjectName,
                        'OnAgentAction',
                        payload
                    );
                }
            }

            // Platform assigns a session ID after AGENT_CONFIGURE
            if (event.data.type === 'AGENT_SESSION_START') {
                var d = event.data.data;
                if (d && d.sessionId) {
                    RcadiaAgentState.sessionId = d.sessionId;
                    if (window.unityInstance) {
                        window.unityInstance.SendMessage(
                            RcadiaAgentState.gameObjectName,
                            'OnSessionStart',
                            d.sessionId
                        );
                    }
                }
            }

            // Platform relays an action rejection back to the game
            if (event.data.type === 'AGENT_ACTION_REJECTED') {
                var d = event.data.data;
                if (d && window.unityInstance) {
                    var payload = d.playerId + '|' + (d.reason || 'Unknown reason');
                    window.unityInstance.SendMessage(
                        RcadiaAgentState.gameObjectName,
                        'OnActionRejected',
                        payload
                    );
                }
            }

            // Platform signals a player timed out
            if (event.data.type === 'AGENT_TIMEOUT') {
                var d = event.data.data;
                if (d && d.playerId && window.unityInstance) {
                    window.unityInstance.SendMessage(
                        RcadiaAgentState.gameObjectName,
                        'OnTimeout',
                        d.playerId
                    );
                }
            }
        });
    },

    // ─── Outbound: Game → Platform ───────────────────────────────────

    RcadiaAgent_Configure: function (configJsonPtr) {
        _RcadiaAgent_Init();
        var json = UTF8ToString(configJsonPtr);
        var config;
        try { config = JSON.parse(json); } catch (e) { config = {}; }
        window.parent.postMessage({
            type: 'AGENT_CONFIGURE',
            data: config
        }, '*');
    },

    RcadiaAgent_SetState: function (stateJsonPtr) {
        var json = UTF8ToString(stateJsonPtr);
        var state;
        try { state = JSON.parse(json); } catch (e) { state = json; }
        window.parent.postMessage({
            type: 'AGENT_STATE',
            data: {
                sessionId: RcadiaAgentState.sessionId,
                state: state,
                timestamp: Date.now()
            }
        }, '*');
    },

    RcadiaAgent_SetObservation: function (playerIdPtr, observationJsonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var json = UTF8ToString(observationJsonPtr);
        var observation;
        try { observation = JSON.parse(json); } catch (e) { observation = json; }
        window.parent.postMessage({
            type: 'AGENT_OBSERVATION',
            data: {
                sessionId: RcadiaAgentState.sessionId,
                playerId: playerId,
                observation: observation,
                timestamp: Date.now()
            }
        }, '*');
    },

    RcadiaAgent_SetLegalActions: function (playerIdPtr, actionsJsonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var json = UTF8ToString(actionsJsonPtr);
        var actions;
        try { actions = JSON.parse(json); } catch (e) { actions = []; }
        window.parent.postMessage({
            type: 'AGENT_LEGAL_ACTIONS',
            data: {
                sessionId: RcadiaAgentState.sessionId,
                playerId: playerId,
                actions: actions,
                timestamp: Date.now()
            }
        }, '*');
    },

    RcadiaAgent_RejectAction: function (playerIdPtr, reasonPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        var reason = UTF8ToString(reasonPtr);
        window.parent.postMessage({
            type: 'AGENT_ACTION_REJECTED',
            data: {
                sessionId: RcadiaAgentState.sessionId,
                playerId: playerId,
                reason: reason,
                timestamp: Date.now()
            }
        }, '*');
    },

    RcadiaAgent_EndGame: function (resultJsonPtr) {
        var json = UTF8ToString(resultJsonPtr);
        var result;
        try { result = JSON.parse(json); } catch (e) { result = {}; }
        window.parent.postMessage({
            type: 'AGENT_GAME_OVER',
            data: {
                sessionId: RcadiaAgentState.sessionId,
                result: result,
                timestamp: Date.now()
            }
        }, '*');
    }
};

autoAddDeps(RcadiaAgentBridgeLib, '$RcadiaAgentState');
mergeInto(LibraryManager.library, RcadiaAgentBridgeLib);
