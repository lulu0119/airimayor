# map_image omits stop-to-stop transit overlays

Status: accepted

`map_image` does not draw transit route geometry. Stop-to-stop chords are not
the path vehicles drive; they hid the road network the vision path must see.
Physical rails stay as TrackData. This beat keeping the overlay until an
on-road path port ([0019](./0019-vendored-map-source.md)). OSM Carto’s default
street map also omits bus route overlays. Line facts stay on `map_text` and
the transit tools.
