# ACP previews are the in-game thumbnail

Status: accepted

An external mayor's chat row uses the same JPEG thumbnail as the built-in mayor ([0014](./0014-tool-image-preview.md)). The game already has that thumbnail when the tool returns. The ACP tool call and the invoke arrive apart, so the session pairs them by tool name and arguments, then puts the same data URI on the tool row. The model still receives the full PNG. The image block an agent may echo is not the preview.

## Considered Options

- **Read the image back from the agent's tool content.** Rejected: that block is optional, and it is the full PNG. The event channel already refused full images.
