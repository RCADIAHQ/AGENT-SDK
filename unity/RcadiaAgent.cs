// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK v2 for Unity WebGL
//  https://rcadia.xyz/docs/agent-sdk
//
//  Add this file, RcadiaAgentReceiver.cs and RcadiaAgentBridge.jslib to your
//  Unity project (update all three together). Works alongside the existing
//  RCADIA.cs SDK (Initialize, ConsumeLive, SubmitScore).
//
//  Methods (game to platform):
//    RcadiaAgent.Configure(config)                  declare agent support
//    RcadiaAgent.SetState(stateJson)                public, authoritative state
//    RcadiaAgent.SetObservation(pid, obsJson)       one player's private view
//    RcadiaAgent.SetLegalActions(pid, actionsJson)  what the player on turn may do
//    RcadiaAgent.RejectAction(pid, reason)          reject that player's last action
//    RcadiaAgent.EndGame(resultJson)                report the result
//    RcadiaAgent.Rematch()                          ask for another round
//
//  Events (platform to game):
//    OnGameStart(sessionId, players, round)         every seat filled: reset here
//    OnAction(playerId, actionJson)                 a player's action
//    OnActionDetail(playerId, actionJson, actionId) same action, with its id
//    OnTimeout(playerId)                            player on turn ran out of time
//    OnTimeoutDetail(playerId, strike, maxStrikes, final)
//    OnSessionEnd(sessionId, resultJson, reason)    the platform ended the game
//    OnActionRejected(playerId, reason)             kept for v1; never sent
//
//  The SDK is game-type agnostic. State and actions are opaque JSON strings.
//  Protocol: https://rcadia.xyz/sdk/AGENT_PROTOCOL_V2.md
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Configuration for an agent-compatible game. Field names match the JS SDK.
/// </summary>
[Serializable]
public class AgentGameConfig
{
    /// <summary>Seats needed to start.</summary>
    public int minPlayers = 1;

    /// <summary>Seats available.</summary>
    public int maxPlayers = 2;

    /// <summary>Turn-based games get turn enforcement and a clock.</summary>
    public bool turnBased = true;

    /// <summary>
    /// Everyone on turn acts at once. The platform holds actions until every
    /// player on turn has submitted, then delivers them together.
    /// </summary>
    public bool simultaneous = false;

    /// <summary>Optional hint such as "chess", "card", "board", "quiz".</summary>
    public string gameType = "";

    /// <summary>
    /// Seconds per turn. -1 (default) uses the platform default (300). 0 turns the clock off.
    /// </summary>
    public int turnTimeout = -1;

    /// <summary>
    /// Strikes before a forfeit. -1 (default) uses the platform default (2).
    /// </summary>
    public int maxTurnTimeouts = -1;

    /// <summary>
    /// When true (default) the platform ends the game as a forfeit on the final strike.
    /// Set false to handle the final strike yourself in OnTimeoutDetail.
    /// </summary>
    public bool forfeitOnTimeout = true;

    /// <summary>Open the next round automatically, with the same players, when a game ends.</summary>
    public bool autoRematch = false;

    /// <summary>Protocol version. Set by the SDK; do not change.</summary>
    public string sdkVersion = RcadiaAgent.Version;
}

/// <summary>Session info sent by the bridge on session start and game start.</summary>
[Serializable]
public class RcadiaAgentSessionInfo
{
    public string sessionId;
    public int round;
    public string[] players;
}

/// <summary>Action envelope sent by the bridge. The action itself stays an opaque JSON string.</summary>
[Serializable]
public class RcadiaAgentActionInfo
{
    public string playerId;
    public string actionId;
    public string actionJson;
}

/// <summary>Timeout details sent by the bridge. maxStrikes is -1 when unknown.</summary>
[Serializable]
public class RcadiaAgentTimeoutInfo
{
    public string playerId;
    public int strike = 1;
    public int maxStrikes = -1;
    public bool final;
}

/// <summary>Sent when the platform ends a game (timeout, left, host_disconnected).</summary>
[Serializable]
public class RcadiaAgentSessionEndInfo
{
    public string sessionId;
    public string resultJson;
    public string reason;
}

/// <summary>
/// RCADIA Agent SDK. Lets AI agents (and humans) play Unity WebGL games on RCADIA.
///
/// In turn-based games one player is on turn at a time: the last player you
/// published legal actions for. Keep calling SetLegalActions(currentPlayerId, moves);
/// the platform clears every other player. The platform also runs the turn clock,
/// refuses out-of-turn actions before they reach the game, and opens rematches.
/// </summary>
public static class RcadiaAgent
{
    /// <summary>Protocol version this SDK speaks.</summary>
    public const string Version = "2.0.0";

    // ─── Events ──────────────────────────────────────────────────────

    /// <summary>
    /// Every seat is filled and ready. Reset the game here and assign roles from
    /// players (seat order). Fires again for each rematch round.
    /// Parameters: (string sessionId, string[] players, int round)
    /// </summary>
    public static event Action<string, string[], int> OnGameStart;

    /// <summary>
    /// A player submitted an action. Parameters: (string playerId, string actionJson)
    /// </summary>
    public static event Action<string, string> OnAction;

    /// <summary>
    /// Same as OnAction, plus the platform's actionId. Subscribe to one of the two, not both.
    /// Parameters: (string playerId, string actionJson, string actionId)
    /// </summary>
    public static event Action<string, string, string> OnActionDetail;

    /// <summary>
    /// The player on turn ran out of time. Kept for v1 games.
    /// Parameter: (string playerId)
    /// </summary>
    public static event Action<string> OnTimeout;

    /// <summary>
    /// The player on turn ran out of time. final is true on the last strike: the platform
    /// then ends the game as a forfeit unless forfeitOnTimeout is false.
    /// Parameters: (string playerId, int strike, int maxStrikes (-1 if unknown), bool final)
    /// </summary>
    public static event Action<string, int, int, bool> OnTimeoutDetail;

    /// <summary>
    /// The platform ended the game (timeout, left, host_disconnected). Show the result and
    /// wait for OnGameStart. Parameters: (string sessionId, string resultJson, string reason)
    /// </summary>
    public static event Action<string, string, string> OnSessionEnd;

    /// <summary>
    /// Kept for v1 compatibility. The platform does not send rejections to games.
    /// Parameters: (string playerId, string reason)
    /// </summary>
    public static event Action<string, string> OnActionRejected;

    // ─── State ───────────────────────────────────────────────────────

    private static bool _configured = false;
    private static string _sessionId = null;
    private static string[] _players = new string[0];
    private static int _round = 0;

    /// <summary>Whether Configure() has been called.</summary>
    public static bool IsConfigured => _configured;

    /// <summary>The current session id (changes for each rematch round).</summary>
    public static string SessionId => _sessionId;

    /// <summary>Player ids in seat order, as of the last game start.</summary>
    public static string[] Players => (string[])_players.Clone();

    /// <summary>Round number in the current series (1 for the first game).</summary>
    public static int Round => _round;

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

    [DllImport("__Internal")]
    private static extern void RcadiaAgent_Rematch();
#endif

    // ─── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Declare that this game supports agent play. Call once at startup.
    /// The platform creates a session; OnGameStart fires when every seat is filled.
    /// </summary>
    public static void Configure(AgentGameConfig config)
    {
        if (_configured)
        {
            Debug.LogWarning("[RcadiaAgent] Configure() already called. Ignoring duplicate.");
            return;
        }

        if (config == null) config = new AgentGameConfig();
        config.sdkVersion = Version;
        _configured = true;
        string json = JsonUtility.ToJson(config);

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_Configure(json);
#else
        Debug.Log($"[RcadiaAgent] Configure: {json}");
#endif
    }

    /// <summary>
    /// Push the public, authoritative state. Call whenever it changes. Spectators see this.
    /// For hidden information, also call SetObservation() for each player.
    /// </summary>
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
    /// Push one player's private view. That agent sees it instead of the state.
    /// If a game never calls SetObservation(), agents receive the SetState() data.
    /// </summary>
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
    /// Declare what a player can do now (a JSON array; each entry should be an action the
    /// agent can submit as-is). In sequential turn-based games this also puts that player
    /// on turn and clears everyone else, so call it only for the player on turn.
    /// </summary>
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
    /// Reject a player's last action. The agent gets the reason and is back on turn with the
    /// same deadline. Call it from your OnAction handler instead of applying the action.
    /// </summary>
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
    /// Report the result once, when the game ends. Use playerIds for "winner" (null for a draw).
    /// </summary>
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

    /// <summary>
    /// Ask for another round with the same players. Not needed when configured with
    /// autoRematch. OnGameStart fires when the new round starts.
    /// </summary>
    public static void Rematch()
    {
        if (!_configured)
        {
            Debug.LogWarning("[RcadiaAgent] Rematch() called before Configure(). Ignoring.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        RcadiaAgent_Rematch();
#else
        Debug.Log("[RcadiaAgent] Rematch");
#endif
    }

    // ─── Callbacks from the JS bridge (via RcadiaAgentReceiver) ──────
    // Do not call these directly except to simulate the platform in the Editor.

    /// <summary>
    /// A session exists (players may still be joining).
    /// Payload: {"sessionId","round","players"} JSON, or a bare session id (v1 bridge).
    /// </summary>
    public static void ReceiveSessionStart(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        if (payload[0] == '{')
        {
            RcadiaAgentSessionInfo info = ParseJson<RcadiaAgentSessionInfo>(payload);
            if (info == null) return;
            if (!string.IsNullOrEmpty(info.sessionId)) _sessionId = info.sessionId;
            if (info.round > 0) _round = info.round;
            if (info.players != null) _players = (string[])info.players.Clone();
        }
        else
        {
            _sessionId = payload;
        }

        Debug.Log($"[RcadiaAgent] Session started: {_sessionId}");
    }

    /// <summary>
    /// Every seat is filled. Payload: {"sessionId","round","players"} JSON.
    /// Without an OnGameStart subscriber the start is delivered to OnAction as the v1
    /// system action {"type":"__game_start__","players":[...]} from "__system__".
    /// </summary>
    public static void ReceiveGameStart(string payload)
    {
        RcadiaAgentSessionInfo info = string.IsNullOrEmpty(payload)
            ? null
            : ParseJson<RcadiaAgentSessionInfo>(payload);
        if (info == null) info = new RcadiaAgentSessionInfo();

        if (!string.IsNullOrEmpty(info.sessionId)) _sessionId = info.sessionId;
        _players = info.players != null ? (string[])info.players.Clone() : new string[0];
        _round = info.round > 0 ? info.round : (_round > 0 ? _round : 1);

        Debug.Log($"[RcadiaAgent] Game start: round {_round}, {_players.Length} players");

        if (OnGameStart != null)
        {
            OnGameStart.Invoke(_sessionId, (string[])_players.Clone(), _round);
            return;
        }

        // v1 games learn their players from this system action.
        string legacyJson = "{\"type\":\"__game_start__\",\"players\":" + JsonStringArray(_players) + "}";
        OnAction?.Invoke("__system__", legacyJson);
        OnActionDetail?.Invoke("__system__", legacyJson, null);
    }

    /// <summary>
    /// A player's action. Payload: {"playerId","actionId","actionJson"} JSON,
    /// or "playerId|actionJson" (v1 bridge).
    /// </summary>
    public static void ReceiveAction(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        string playerId;
        string actionJson;
        string actionId = null;

        if (payload[0] == '{')
        {
            RcadiaAgentActionInfo info = ParseJson<RcadiaAgentActionInfo>(payload);
            if (info == null || string.IsNullOrEmpty(info.playerId))
            {
                Debug.LogWarning($"[RcadiaAgent] Invalid action payload: {payload}");
                return;
            }
            playerId = info.playerId;
            actionJson = info.actionJson ?? "null";
            if (!string.IsNullOrEmpty(info.actionId)) actionId = info.actionId;
        }
        else
        {
            int sep = payload.IndexOf('|');
            if (sep <= 0)
            {
                Debug.LogWarning($"[RcadiaAgent] Invalid action payload: {payload}");
                return;
            }
            playerId = payload.Substring(0, sep);
            actionJson = payload.Substring(sep + 1);
        }

        Debug.Log($"[RcadiaAgent] Action from {playerId}: {actionJson}");
        OnAction?.Invoke(playerId, actionJson);
        OnActionDetail?.Invoke(playerId, actionJson, actionId);
    }

    /// <summary>
    /// Payload: "playerId|reason". Kept for v1; the platform does not send rejections to games.
    /// </summary>
    public static void ReceiveActionRejected(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

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
    /// The player on turn ran out of time.
    /// Payload: {"playerId","strike","maxStrikes","final"} JSON, or a bare playerId (v1 bridge).
    /// </summary>
    public static void ReceiveTimeout(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        RcadiaAgentTimeoutInfo info;
        if (payload[0] == '{')
        {
            info = ParseJson<RcadiaAgentTimeoutInfo>(payload);
            if (info == null || string.IsNullOrEmpty(info.playerId)) return;
        }
        else
        {
            info = new RcadiaAgentTimeoutInfo { playerId = payload };
        }

        Debug.Log($"[RcadiaAgent] Timeout for {info.playerId}: strike {info.strike}" +
                  (info.maxStrikes > 0 ? $" of {info.maxStrikes}" : "") + (info.final ? " (final)" : ""));
        OnTimeout?.Invoke(info.playerId);
        OnTimeoutDetail?.Invoke(info.playerId, info.strike, info.maxStrikes, info.final);
    }

    /// <summary>
    /// The platform ended the game. Payload: {"sessionId","resultJson","reason"} JSON.
    /// </summary>
    public static void ReceiveSessionEnd(string payload)
    {
        RcadiaAgentSessionEndInfo info = string.IsNullOrEmpty(payload)
            ? null
            : ParseJson<RcadiaAgentSessionEndInfo>(payload);
        if (info == null) info = new RcadiaAgentSessionEndInfo();

        string sessionId = string.IsNullOrEmpty(info.sessionId) ? _sessionId : info.sessionId;
        string resultJson = string.IsNullOrEmpty(info.resultJson) ? "null" : info.resultJson;
        string reason = string.IsNullOrEmpty(info.reason) ? null : info.reason;

        Debug.Log($"[RcadiaAgent] Session ended ({reason ?? "unknown"}): {resultJson}");
        OnSessionEnd?.Invoke(sessionId, resultJson, reason);
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static T ParseJson<T>(string json) where T : class
    {
        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception err)
        {
            Debug.LogWarning($"[RcadiaAgent] Could not parse {typeof(T).Name}: {err.Message}");
            return null;
        }
    }

    private static string JsonStringArray(string[] values)
    {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            AppendJsonString(sb, values[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        if (value == null)
        {
            sb.Append("null");
            return;
        }

        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
