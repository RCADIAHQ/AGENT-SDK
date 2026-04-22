// ═══════════════════════════════════════════════════════════════════════
//  RCADIA Agent SDK — Message Receiver MonoBehaviour
//  https://rcadia.xyz
//
//  Attach this to a GameObject named "RcadiaAgentReceiver" in your scene.
//  It receives messages from the JavaScript bridge and routes them to
//  the RcadiaAgent static class.
//
//  Inbound messages from JS bridge:
//    OnAgentAction(payload)      — agent submitted an action
//    OnSessionStart(sessionId)   — platform assigned a session
//    OnActionRejected(payload)   — action was rejected
//    OnTimeout(playerId)         — player timed out
// ═══════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// Attach to a GameObject named "RcadiaAgentReceiver" in your scene.
/// Bridges JS postMessage events to the RcadiaAgent static API.
/// </summary>
public class RcadiaAgentReceiver : MonoBehaviour
{
    /// <summary>
    /// Called from JS bridge when an agent or human submits an action.
    /// Payload format: "playerId|actionJson"
    /// </summary>
    public void OnAgentAction(string payload)
    {
        RcadiaAgent.ReceiveAction(payload);
    }

    /// <summary>
    /// Called from JS bridge when the platform assigns a session ID.
    /// </summary>
    public void OnSessionStart(string sessionId)
    {
        RcadiaAgent.ReceiveSessionStart(sessionId);
    }

    /// <summary>
    /// Called from JS bridge when an action is rejected.
    /// Payload format: "playerId|reason"
    /// </summary>
    public void OnActionRejected(string payload)
    {
        RcadiaAgent.ReceiveActionRejected(payload);
    }

    /// <summary>
    /// Called from JS bridge when a player's turn times out.
    /// </summary>
    public void OnTimeout(string playerId)
    {
        RcadiaAgent.ReceiveTimeout(playerId);
    }
}
