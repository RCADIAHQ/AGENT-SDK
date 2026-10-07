// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK v2: message receiver MonoBehaviour
//  https://rcadia.xyz/docs/agent-sdk
//
//  Attach this to a GameObject named "RcadiaAgentReceiver" in every scene
//  that plays agent games. RcadiaAgentBridge.jslib calls these methods with
//  SendMessage; each one forwards to the RcadiaAgent static class.
//
//  Messages from the JS bridge:
//    OnSessionStart(json)     a session exists      {"sessionId","round","players"}
//    OnGameStart(json)        every seat is filled  {"sessionId","round","players"}
//    OnAgentAction(json)      a player's action     {"playerId","actionId","actionJson"}
//    OnTimeout(json)          player out of time    {"playerId","strike","maxStrikes","final"}
//    OnSessionEnd(json)       platform ended game   {"sessionId","resultJson","reason"}
//    OnActionRejected(text)   kept for v1           "playerId|reason"
//
//  The v1 payload formats (a bare session id, "playerId|actionJson", a bare
//  playerId) are still accepted.
// ═══════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// Attach to a GameObject named "RcadiaAgentReceiver".
/// Bridges JS postMessage events to the RcadiaAgent static API.
/// </summary>
public class RcadiaAgentReceiver : MonoBehaviour
{
    /// <summary>A session exists (players may still be joining).</summary>
    public void OnSessionStart(string payload)
    {
        RcadiaAgent.ReceiveSessionStart(payload);
    }

    /// <summary>Every seat is filled and ready. Fires RcadiaAgent.OnGameStart.</summary>
    public void OnGameStart(string payload)
    {
        RcadiaAgent.ReceiveGameStart(payload);
    }

    /// <summary>A player's action. Fires RcadiaAgent.OnAction and OnActionDetail.</summary>
    public void OnAgentAction(string payload)
    {
        RcadiaAgent.ReceiveAction(payload);
    }

    /// <summary>The player on turn ran out of time. Fires OnTimeout and OnTimeoutDetail.</summary>
    public void OnTimeout(string payload)
    {
        RcadiaAgent.ReceiveTimeout(payload);
    }

    /// <summary>The platform ended the game. Fires RcadiaAgent.OnSessionEnd.</summary>
    public void OnSessionEnd(string payload)
    {
        RcadiaAgent.ReceiveSessionEnd(payload);
    }

    /// <summary>Kept for v1 compatibility. Payload format: "playerId|reason".</summary>
    public void OnActionRejected(string payload)
    {
        RcadiaAgent.ReceiveActionRejected(payload);
    }
}
