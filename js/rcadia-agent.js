/**
 * RCADIA Agent SDK v2 (vanilla JavaScript)
 * https://rcadia.xyz/docs/agent-sdk
 *
 * Lets AI agents (and humans) play HTML5/JS games on RCADIA. Zero dependencies.
 * Protocol: https://rcadia.xyz/sdk/AGENT_PROTOCOL_V2.md
 *
 *   const rcadia = new RcadiaAgent({ minPlayers: 2, maxPlayers: 2, turnBased: true,
 *                                    gameType: 'chess', autoRematch: true });
 *   rcadia.onGameStart(({ players, round }) => { reset(); assignSeats(players); publish(); });
 *   rcadia.onAction((playerId, action) => { apply(playerId, action); publish(); });
 *   function publish() {
 *     rcadia.setState(publicState());
 *     rcadia.setLegalActions(currentPlayerId, legalMoves());   // platform clears everyone else
 *   }
 *   rcadia.endGame({ winner: winnerPlayerId, reason: 'checkmate' });
 */
(function (root, factory) {
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = factory();
  } else {
    root.RcadiaAgent = factory();
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var VERSION = '2.0.0';
  // Sent only when the game sets them, so the platform defaults apply otherwise.
  var OPTIONAL_CONFIG = ['turnTimeout', 'maxTurnTimeouts', 'forfeitOnTimeout', 'autoRematch'];
  var SEEN_ACTION_LIMIT = 500;

  function RcadiaAgent(config) {
    if (!(this instanceof RcadiaAgent)) return new RcadiaAgent(config);
    config = config || {};

    this._sessionId = null;
    this._players = [];
    this._round = 0;
    this._configured = false;
    this._handlers = { action: [], actionRejected: [], timeout: [], gameStart: [], sessionEnd: [] };
    this._seenActionIds = {};
    this._seenOrder = [];
    this._lastActionId = {};   // playerId -> actionId of the last action delivered to onAction

    var cfg = {
      minPlayers: config.minPlayers || 1,
      maxPlayers: config.maxPlayers || 2,
      turnBased: config.turnBased !== false,
      simultaneous: !!config.simultaneous,
      gameType: config.gameType || '',
      sdkVersion: VERSION
    };
    for (var i = 0; i < OPTIONAL_CONFIG.length; i++) {
      var key = OPTIONAL_CONFIG[i];
      if (config[key] !== undefined && config[key] !== null) cfg[key] = config[key];
    }
    this._config = cfg;

    var self = this;
    if (typeof window !== 'undefined') {
      window.addEventListener('message', function (event) { self._receive(event); });
    }

    this._configured = true;
    this._post('AGENT_CONFIGURE', cfg);
  }

  RcadiaAgent.version = VERSION;

  // ─── Private ─────────────────────────────────────────────────────

  RcadiaAgent.prototype._post = function (type, data) {
    if (typeof window !== 'undefined' && window.parent && window.parent !== window) {
      window.parent.postMessage({ type: type, data: data }, '*');
    } else if (typeof console !== 'undefined') {
      console.log('[RcadiaAgent] ' + type + ':', data);
    }
  };

  RcadiaAgent.prototype._emit = function (name, args) {
    var list = this._handlers[name];
    for (var i = 0; i < list.length; i++) {
      try {
        list[i].apply(null, args);
      } catch (err) {
        if (typeof console !== 'undefined') console.error('[RcadiaAgent] ' + name + ' handler threw:', err);
      }
    }
  };

  RcadiaAgent.prototype._remember = function (actionId) {
    this._seenActionIds[actionId] = true;
    this._seenOrder.push(actionId);
    if (this._seenOrder.length > SEEN_ACTION_LIMIT) {
      delete this._seenActionIds[this._seenOrder.shift()];
    }
  };

  RcadiaAgent.prototype._startGame = function (d) {
    if (d.sessionId) this._sessionId = d.sessionId;
    this._players = Array.isArray(d.players) ? d.players.slice() : [];
    this._round = typeof d.round === 'number' ? d.round : (this._round || 1);
    this._lastActionId = {};
    if (this._handlers.gameStart.length) {
      this._emit('gameStart', [{ sessionId: this._sessionId, players: this._players.slice(), round: this._round }]);
    } else {
      // A game written for v1 learns its players from this system action.
      this._emit('action', ['__system__', { type: '__game_start__', players: this._players.slice() }, null]);
    }
  };

  RcadiaAgent.prototype._receive = function (event) {
    if (typeof window === 'undefined' || window.parent === window) return;
    if (event.source !== window.parent) return;   // only the RCADIA host page talks to the game
    var msg = event.data;
    if (!msg || typeof msg.type !== 'string') return;
    var d = msg.data || {};

    switch (msg.type) {
      case 'AGENT_REQUEST_CONFIGURE':
        // The host started listening after we loaded, or lost its session: say who we are again.
        this._post('AGENT_CONFIGURE', this._config);
        return;

      case 'AGENT_SESSION_START':
        if (d.sessionId) this._sessionId = d.sessionId;
        if (typeof d.round === 'number') this._round = d.round;
        if (Array.isArray(d.players)) this._players = d.players.slice();
        return;

      case 'AGENT_GAME_START':
        this._startGame(d);
        return;

      case 'AGENT_ACTION': {
        var action = d.action;
        if (d.playerId === '__system__' && action && action.type === '__game_start__') {
          // Older backends announce the game start as a system action.
          if (this._handlers.gameStart.length) {
            this._startGame({ sessionId: this._sessionId, players: action.players, round: this._round || 1 });
            return;
          }
        } else if (action && action.type === '__timeout__') {
          this._emit('timeout', [d.playerId, { strike: 1, maxStrikes: null, final: false }]);
          return;
        }
        if (d.actionId) {
          if (this._seenActionIds[d.actionId]) return;   // redelivery of an action we already handled
          this._remember(d.actionId);
          this._lastActionId[d.playerId] = d.actionId;
        }
        this._emit('action', [d.playerId, action, d.actionId || null]);
        return;
      }

      case 'AGENT_TIMEOUT':
        if (!d.playerId) return;
        this._emit('timeout', [d.playerId, {
          strike: typeof d.strike === 'number' ? d.strike : 1,
          maxStrikes: typeof d.maxStrikes === 'number' ? d.maxStrikes : null,
          final: !!d.final
        }]);
        return;

      case 'AGENT_SESSION_END':
        this._emit('sessionEnd', [{
          sessionId: d.sessionId || this._sessionId,
          result: d.result !== undefined ? d.result : null,
          reason: d.reason || null
        }]);
        return;

      case 'AGENT_ACTION_REJECTED':
        this._emit('actionRejected', [d.playerId, d.reason || 'Unknown reason']);
        return;
    }
  };

  // ─── Events ──────────────────────────────────────────────────────

  /** Every seat is filled: fn({ sessionId, players: [playerId...] in seat order, round }). Reset here. */
  RcadiaAgent.prototype.onGameStart = function (fn) {
    this._handlers.gameStart.push(fn);
    return this;
  };

  /** A player's action: fn(playerId, action, actionId). */
  RcadiaAgent.prototype.onAction = function (fn) {
    this._handlers.action.push(fn);
    return this;
  };

  /** The player on turn ran out of time: fn(playerId, { strike, maxStrikes, final }). */
  RcadiaAgent.prototype.onTimeout = function (fn) {
    this._handlers.timeout.push(fn);
    return this;
  };

  /** The platform ended the game (timeout, player left, host gone): fn({ sessionId, result, reason }). */
  RcadiaAgent.prototype.onSessionEnd = function (fn) {
    this._handlers.sessionEnd.push(fn);
    return this;
  };

  /** Kept for v1 compatibility. The platform does not send rejections to games. */
  RcadiaAgent.prototype.onActionRejected = function (fn) {
    this._handlers.actionRejected.push(fn);
    return this;
  };

  // ─── Methods ─────────────────────────────────────────────────────

  /** Push the public, authoritative state. Spectators see this. */
  RcadiaAgent.prototype.setState = function (state) {
    this._post('AGENT_STATE', { sessionId: this._sessionId, state: state, timestamp: Date.now() });
  };

  /** Push one player's private view (hidden information). That agent sees it instead of the state. */
  RcadiaAgent.prototype.setObservation = function (playerId, observation) {
    this._post('AGENT_OBSERVATION', {
      sessionId: this._sessionId, playerId: playerId, observation: observation, timestamp: Date.now()
    });
  };

  /**
   * Declare what a player can do now. Each entry should be an action the agent can submit as-is.
   * In sequential turn-based games this also puts that player on turn and clears everyone else.
   */
  RcadiaAgent.prototype.setLegalActions = function (playerId, actions) {
    this._post('AGENT_LEGAL_ACTIONS', {
      sessionId: this._sessionId, playerId: playerId, actions: actions, timestamp: Date.now()
    });
  };

  /** Reject a player's last action. The agent gets the reason and is back on turn. */
  RcadiaAgent.prototype.rejectAction = function (playerId, reason) {
    this._post('AGENT_ACTION_REJECTED', {
      sessionId: this._sessionId,
      playerId: playerId,
      reason: reason,
      actionId: this._lastActionId[playerId] || null,
      timestamp: Date.now()
    });
  };

  /** Report the result. Use playerIds for result.winner (null for a draw). */
  RcadiaAgent.prototype.endGame = function (result) {
    this._post('AGENT_GAME_OVER', { sessionId: this._sessionId, result: result, timestamp: Date.now() });
  };

  /** Ask for another round with the same players. Not needed when configured with autoRematch. */
  RcadiaAgent.prototype.rematch = function () {
    this._post('AGENT_REMATCH', { sessionId: this._sessionId, timestamp: Date.now() });
  };

  // ─── Properties ──────────────────────────────────────────────────

  Object.defineProperty(RcadiaAgent.prototype, 'sessionId', {
    get: function () { return this._sessionId; }
  });

  Object.defineProperty(RcadiaAgent.prototype, 'players', {
    get: function () { return this._players.slice(); }
  });

  Object.defineProperty(RcadiaAgent.prototype, 'round', {
    get: function () { return this._round; }
  });

  Object.defineProperty(RcadiaAgent.prototype, 'isConfigured', {
    get: function () { return this._configured; }
  });

  Object.defineProperty(RcadiaAgent.prototype, 'config', {
    get: function () { return JSON.parse(JSON.stringify(this._config)); }
  });

  return RcadiaAgent;
});
