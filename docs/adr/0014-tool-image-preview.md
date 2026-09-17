# Tool image previews are thumbnails over the event channel

Status: accepted

Tool rows showed image results as a `{"saved":"..."}` disk path: the player
saw nothing. Preview images travel as a JPEG thumbnail (480px, quality 60)
in a new optional `image` data-URI field on the tool done event, rendered by
the tool row. It beat two alternatives: `file://` URLs (Gameface does not
serve arbitrary disk paths) and full-resolution base64 (megabytes per event).

The thumbnail is produced on the main thread at capture time
(`CS2MCP.ToolPreview`, shared by screenshot and `map_image`) and carried on
`BridgeResponse.Preview`; base64 over the binding is pure .NET on the agent
thread. Preview is UI-only and best-effort: the model still gets the full PNG
from disk, and any preview failure leaves a text-only result. Snapshots do
not persist previews; session switch rehydrates text only.
