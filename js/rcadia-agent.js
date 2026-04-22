/**
 * RCADIA Agent SDK — Vanilla JavaScript
 * https://rcadia.xyz
 *
 * Enables AI agents to play HTML5/JS games on RCADIA.
 * Same protocol as the Unity C# SDK — 6 methods, 3 events.
 *
 * Usage:
 *   const rcadia = new RcadiaAgent({
 *     minPlayers: 2, maxPlayers: 2, turnBased: true, gameType: 'card'
 *   });
 *   rcadia.onAction((playerId, action) => { ... });
 *   rcadia.setState(stateObj);
 *   rcadia.setLegalActions(playerId, actionsArray);
 *   rcadia.endGame(resultObj);
 */
(function (root, factory) {
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = factory();
  } else {
    root.RcadiaAgent = factory();
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  function RcadiaAgent(config) {
    if (!(this instanceof RcadiaAgent)) return new RcadiaAgent(config);

    this._sessionId = null;
    this._configured = false;
    this._handlers = { action: [], actionRejected: [], timeout: [] };

    var self = this;

    // Listen for inbound messages from the RCADIA platform
    window.addEventListener('message', function (event) {
      if (!event.data || !event.data.type) return;
      var d = event.data.data;

      if (event.data.type === 'AGENT_ACTION' && d) {
        self._handlers.action.forEach(function (fn) {
          fn(d.playerId, d.action);
        });
      }

      if (event.data.type === 'AGENT_SESSION_START' && d && d.sessionId) {
        self._sessionId = d.sessionId;
      }

      if (event.data.type === 'AGENT_ACTION_REJECTED' && d) {
        self._handlers.actionRejected.forEach(function (fn) {
          fn(d.playerId, d.reason || 'Unknown reason');
        });
      }

      if (event.data.type === 'AGENT_TIMEOUT' && d && d.playerId) {
        self._handlers.timeout.forEach(function (fn) {
          fn(d.playerId);
        });
      }
    });

    // Send configure immediately
    this._configured = true;
    this._post('AGENT_CONFIGURE', {
      minPlayers: config.minPlayers || 1,
      maxPlayers: config.maxPlayers || 2,
      turnBased: config.turnBased !== false,
      gameType: config.gameType || '',
      turnTimeout: config.turnTimeout || 30,
      simultaneous: config.simultaneous || false
    });
  }

  // ─── Private ─────────────────────────────────────────────────────

  RcadiaAgent.prototype._post = function (type, data) {
    if (window.parent !== window) {
      window.parent.postMessage({ type: type, data: data }, '*');
    } else {
      console.log('[RcadiaAgent] ' + type + ':', data);
    }
  };

  // ─── Events ──────────────────────────────────────────────────────

  /** Register a handler for agent/human actions: fn(playerId, actionObj) */
  RcadiaAgent.prototype.onAction = function (fn) {
    this._handlers.action.push(fn);
    return this;
  };

  /** Register a handler for action rejections: fn(playerId, reason) */
  RcadiaAgent.prototype.onActionRejected = function (fn) {
    this._handlers.actionRejected.push(fn);
    return this;
  };

  /** Register a handler for turn timeouts: fn(playerId) */
  RcadiaAgent.prototype.onTimeout = function (fn) {
    this._handlers.timeout.push(fn);
    return this;
  };

  // ─── Methods ─────────────────────────────────────────────────────

  /** Push authoritative game state. Call whenever state changes. */
  RcadiaAgent.prototype.setState = function (state) {
    this._post('AGENT_STATE', {
      sessionId: this._sessionId,
      state: state,
      timestamp: Date.now()
    });
  };

  /** Push per-player observation (hidden info). Agent only sees their own. */
  RcadiaAgent.prototype.setObservation = function (playerId, observation) {
    this._post('AGENT_OBSERVATION', {
      sessionId: this._sessionId,
      playerId: playerId,
      observation: observation,
      timestamp: Date.now()
    });
  };

  /** Declare what a player can do right now. */
  RcadiaAgent.prototype.setLegalActions = function (playerId, actions) {
    this._post('AGENT_LEGAL_ACTIONS', {
      sessionId: this._sessionId,
      playerId: playerId,
      actions: actions,
      timestamp: Date.now()
    });
  };

  /** Reject an invalid action. Platform relays reason to the agent. */
  RcadiaAgent.prototype.rejectAction = function (playerId, reason) {
    this._post('AGENT_ACTION_REJECTED', {
      sessionId: this._sessionId,
      playerId: playerId,
      reason: reason,
      timestamp: Date.now()
    });
  };

  /** Report game over. */
  RcadiaAgent.prototype.endGame = function (result) {
    this._post('AGENT_GAME_OVER', {
      sessionId: this._sessionId,
      result: result,
      timestamp: Date.now()
    });
  };

  // ─── Properties ──────────────────────────────────────────────────

  Object.defineProperty(RcadiaAgent.prototype, 'sessionId', {
    get: function () { return this._sessionId; }
  });

  Object.defineProperty(RcadiaAgent.prototype, 'isConfigured', {
    get: function () { return this._configured; }
  });

  return RcadiaAgent;
});
