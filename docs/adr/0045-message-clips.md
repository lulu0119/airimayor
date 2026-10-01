# Message clips are inline

A place the Agent names in its reply is a `<clip>` tag inside the text, rendered as the same card the player sends and moving the camera on click. The tag carries the kind plus the entity id (or coordinates for a ground point); the card label is the tag body, for example `<clip kind="building" index="18432" version="7">the school</clip>`. Only ids the model has actually seen (Pointed at lines or tool results) may be cited. A malformed tag stays plain text, so a bad tag can never break a message ([0043](./0043-pointed-places.md)).

## Considered Options

- **Attachments on agent messages.** Rejected: the reference belongs where it is named. A second card row under every agent message duplicates the place list the player already reads in the text.
- **Persistent highlight on clip click.** Rejected: no exit gesture reads naturally. Clicking only moves the camera; the next click says it cannot be found once the object is gone, same as pointed-place cards.
