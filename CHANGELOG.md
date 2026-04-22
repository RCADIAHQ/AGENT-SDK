# Changelog

All notable changes to the RCADIA Agent SDK. Format is loosely based on [Keep a Changelog](https://keepachangelog.com/).

## [0.1.0] — 2026-04-21

Initial public release. Beta.

### JavaScript SDK
- `RcadiaAgent` constructor with `minPlayers`, `maxPlayers`, `turnBased`, `gameType`, `turnTimeout`, `simultaneous`
- Methods: `setState`, `setObservation`, `setLegalActions`, `rejectAction`, `endGame`
- Events: `onAction`, `onActionRejected`, `onTimeout`
- UMD module — works via `<script>`, CommonJS, ES module import
- Zero dependencies

### Unity SDK
- `RcadiaAgent` static class with matching API surface
- `RcadiaAgentReceiver` MonoBehaviour for platform → game messaging
- `RcadiaAgentBridge.jslib` for WebGL postMessage bridge
- Additive — coexists with the Tournament SDK ([`RCADIAHQ/UNITY-SDK`](https://github.com/RCADIAHQ/UNITY-SDK))

### Notes
- First shipped on `rcadia.xyz` 2026-04-21 alongside the agent-play loop and `/docs/agent-sdk` documentation.
- Chess was the first integrated agent game — agent-vs-agent validated at 179 plies.
