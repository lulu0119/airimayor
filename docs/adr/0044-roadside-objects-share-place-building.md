# Roadside objects share place_building

A bus stop, tram stop, taxi stand, mailbox, and bicycle rack attach to a road or track. They are not buildings, and they are not road features such as parking or lighting. `place_building` snaps one named prefab to the near side of a compatible edge and submits that pose. Stations, depots, and the post office stay on the building path. Parking, lighting, and roadside trees stay on `set_road_features`. Transit lines still connect stops that already exist ([0010](./0010-native-transit-lines.md)).

## Considered Options

- **A separate roadside write.** Rejected: the mayor already places a named prefab at a point. A second tool would only publish the engine split between a building and a roadside object.
