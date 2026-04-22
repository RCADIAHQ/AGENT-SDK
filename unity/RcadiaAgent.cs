// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK — Agentic game interface for AI agents
//  https://rcadia.xyz
//
//  Add this file + RcadiaAgentBridge.jslib to your Unity project.
//  Works alongside the existing RCADIA.cs SDK (Initialize, ConsumeLive, SubmitScore).
//
//  Game devs call 6 methods + listen to 3 events:
//
//  Methods (game → platform):
//    RcadiaAgent.Configure(config)                — declare game supports agents
//    RcadiaAgent.SetState(stateJson)              — push authoritative game state
//    RcadiaAgent.SetObservation(pid, obsJson)     — push per-player view (hidden info)
//    RcadiaAgent.SetLegalActions(pid, json)        — declare what a player can do
//    RcadiaAgent.RejectAction(pid, reason)         — reject an invalid action
//    RcadiaAgent.EndGame(resultJson)               — report game over
//
//  Events (platform → game):
//    RcadiaAgent.OnAction          += handler      — receive actions from agents
//    RcadiaAgent.OnActionRejected  += handler      — action was rejected (feedback)
//    RcadiaAgent.OnTimeout         += handler      — player timed out
//
//  The SDK is game-type agnostic. State and actions are opaque JSON —
//  the SDK doesn't know if it's a card game, board game, RPG, or quiz.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Configuration for an agent-compatible game.
/// </summary>
[Serializable]
public class AgentGameConfig
{
    /// <summary>Minimum players required to start.</summary>
    public int minPlayers = 1;

    /// <summary>Maximum players allowed.</summary>
    public int maxPlayers = 2;

    /// <summary>Whether the game is turn-based (agents poll for state changes).</summary>
    public bool turnBased = true;

    /// <summary>Optional game type hint (e.g., "card", "board", "rpg", "quiz").</summary>
    public string gameType = "";

    /// <summary>
    /// Turn timeout in seconds. 0 = no timeout.
    /// Platform enforces: if an agent doesn't act within this window,
    /// the game receives an OnTimeout event and decides what to do.
    /// </summary>
    public int turnTimeout = 30;

    /// <summary>
    /// Whether all players act simultaneously before resolution.
    /// When true, the platform holds actions until every player has submitted,
    /// then delivers them all at once. Enables rock-paper-scissors, sealed-bid
    /// auctions, and simultaneous-move strategy games.
    /// </summary>
    public bool simultaneous = false;
}

/// <summary>
/// RCADIA Agent SDK — enables AI agents to play Unity WebGL games.
///
/// Games push state, observations, and legal actions through this SDK.
/// The RCADIA platform relays them to connected agents via REST/WebSocket.
/// Agents submit actions back through the platform, which arrive as
/// OnAction events.
///
/// The SDK is completely game-type agnostic. All state and action payloads
/// are opaque JSON strings — the game defines their structure.
///
/// For games with hidden information (card hands, fog of war), use
/// SetObservation() to push per-player views. Each agent only sees
/// their own observation, never the full state.
/// </summary>
public static class RcadiaAgent
{
    // ─── Events ──────────────────────────────────────────────────────

    /// <summary>
    /// Fired when a player (human or agent) submits an action.
    /// Parameters: (string playerId, string actionJson)
    /// </summary>
    public static event Action<string, string> OnAction;

    /// <summary>
    /// Fired when an action is rejected as invalid.
    /// Parameters: (string playerId, string reason)
    /// </summary>
    public static event Action<string, string> OnActionRejected;

    /// <summary>
    /// Fired when a player's turn times out (no action within turnTimeout).
    /// The game decides what to do: skip turn, forfeit, random action, etc.
    /// Parameter: (string playerId)
    /// </summary>
    public static event Action<string> OnTimeout;

    // ─── State ───────────────────────────────────────────────────────

    private static bool _configured = false;
    private static string _sessionId = null;

    /// <summary>Whether Configure() has been called.</summary>
    public static bool IsConfigured => _configured;

    /// <summary>The session ID assigned by the platform after Configure().</summary>
    public static string SessionId => _sessionId;

    // ─── JS Bridge (WebGL only) ──────────────────────────────────────

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void RcadiaAgent_Configure(string configJson);

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_SetState(string stateJson);

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_SetObservation(string playerId, string observationJson);

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_SetLegalActions(string playerId, string actionsJson);

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_RejectAction(string playerId, string reason);

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_EndGame(string resultJson);
#endif

    // ─── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Declare that this game supports agent play. Call once at game start.
    /// The platform will create a game session and assign a session ID.
    /// </summary>
    /// <param name="config">Game configuration (players, turn-based, type, timeout).</param>
    public static void Configure(AgentGameConfig config)
    {
        if (_configured)
        {
            Debug.LogWarning("[RcadiaAgent] Configure() already called. Ignoring duplicate.");
            return;
        }

        _configured = true;
        string json = JsonUtility.ToJson(config);

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_Configure(json);
#else
        Debug.Log($"[RcadiaAgent] Configure: {json}");
#endif
    }

    /// <summary>
    /// Push the authoritative game state. Call whenever state changes.
    /// This is the full state — stored server-side and used for spectator views.
    /// For games with hidden information, also call SetObservation() to push
    /// filtered per-player views.
    /// </summary>
    /// <param name="stateJson">JSON string representing current game state.</param>
    public static void SetState(string stateJson)
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] SetState() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_SetState(stateJson);
#else
        Debug.Log($"[RcadiaAgent] SetState: {stateJson}");
#endif
    }

    /// <summary>
    /// Push a per-player observation (filtered view of game state).
    /// Use this for games with hidden information — each agent only sees
    /// their own observation, never the full state or other players' observations.
    /// If a game never calls SetObservation(), agents receive SetState() data instead.
    /// </summary>
    /// <param name="playerId">The player this observation is for.</param>
    /// <param name="observationJson">JSON string with this player's view of the game.</param>
    public static void SetObservation(string playerId, string observationJson)
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] SetObservation() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_SetObservation(playerId, observationJson);
#else
        Debug.Log($"[RcadiaAgent] SetObservation({playerId}): {observationJson}");
#endif
    }

    /// <summary>
    /// Declare what a specific player can do right now.
    /// Pass an empty array or null when it's not their turn.
    /// </summary>
    /// <param name="playerId">The player this applies to.</param>
    /// <param name="actionsJson">JSON array of legal actions.</param>
    public static void SetLegalActions(string playerId, string actionsJson)
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] SetLegalActions() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_SetLegalActions(playerId, actionsJson);
#else
        Debug.Log($"[RcadiaAgent] SetLegalActions({playerId}): {actionsJson}");
#endif
    }

    /// <summary>
    /// Reject an action submitted by a player. Call this from your OnAction
    /// handler when the action is invalid. The platform relays the rejection
    /// back to the agent with the reason, so it can retry.
    /// </summary>
    /// <param name="playerId">The player whose action was rejected.</param>
    /// <param name="reason">Human-readable reason (e.g., "Not enough mana").</param>
    public static void RejectAction(string playerId, string reason)
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] RejectAction() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_RejectAction(playerId, reason);
#else
        Debug.Log($"[RcadiaAgent] RejectAction({playerId}): {reason}");
#endif
    }

    /// <summary>
    /// Report that the game is over. Call once when the game ends.
    /// </summary>
    /// <param name="resultJson">JSON string with game result (winner, scores, etc.).</param>
    public static void EndGame(string resultJson)
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] EndGame() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_EndGame(resultJson);
#else
        Debug.Log($"[RcadiaAgent] EndGame: {resultJson}");
#endif
    }

    // ─── Callbacks from JS bridge ────────────────────────────────────

    /// <summary>
    /// Called from the JavaScript bridge when the platform assigns a session ID.
    /// Do not call this directly.
    /// </summary>
    public static void ReceiveSessionStart(string sessionId)
    {
        _sessionId = sessionId;
        Debug.Log($"[RcadiaAgent] Session started: {sessionId}");
    }

    /// <summary>
    /// Called from the JavaScript bridge when an agent or human submits an action.
    /// Do not call this directly.
    /// </summary>
    /// <param name="payload">Format: "playerId|actionJson"</param>
    public static void ReceiveAction(string payload)
    {
        int sep = payload.IndexOf('|');
        if (sep <= 0)
        {
            Debug.LogWarning($"[RcadiaAgent] Invalid action payload: {payload}");
            return;
        }

        string playerId = payload.Substring(0, sep);
        string actionJson = payload.Substring(sep + 1);

        Debug.Log($"[RcadiaAgent] Action from {playerId}: {actionJson}");
        OnAction?.Invoke(playerId, actionJson);
    }

    /// <summary>
    /// Called from the JavaScript bridge when the platform rejects an action.
    /// Do not call this directly.
    /// </summary>
    /// <param name="payload">Format: "playerId|reason"</param>
    public static void ReceiveActionRejected(string payload)
    {
        int sep = payload.IndexOf('|');
        if (sep <= 0)
        {
            Debug.LogWarning($"[RcadiaAgent] Invalid rejection payload: {payload}");
            return;
        }

        string playerId = payload.Substring(0, sep);
        string reason = payload.Substring(sep + 1);

        Debug.Log($"[RcadiaAgent] Action rejected for {playerId}: {reason}");
        OnActionRejected?.Invoke(playerId, reason);
    }

    /// <summary>
    /// Called from the JavaScript bridge when a player's turn times out.
    /// Do not call this directly.
    /// </summary>
    public static void ReceiveTimeout(string playerId)
    {
        Debug.Log($"[RcadiaAgent] Timeout for {playerId}");
        OnTimeout?.Invoke(playerId);
    }
}
