# Connection shape stays inside build_network

Status: accepted

One `build_network` call is a straight segment, a tangent-locked curve, a smooth two-edge bend, or a parallel copy of the road between two nodes. The caller passes that as `shape` and reads back the realized length, grade, and whether each end joined a node, split an edge, or started a new node. A free control point on a straight segment is gone: a sideways bend is `shape=complex`.

## Considered Options

- **Depend on or vendor CS2-NetworkTools.** Rejected: it is a player tool that takes the active tool and bypasses collision. [0002](./0002-native-validation.md) keeps ordinary validation.
- **A catalog tool per shape.** Rejected: roads, pipes, and cables stay one linear write ([0004](./0004-linear-networks.md)).
- **Keep a control point on the straight segment.** Rejected: that segment is no longer straight, and the curve shapes already place the bend.
