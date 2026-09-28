import assert from "node:assert/strict";
import { applyWireEvent, emptyTranscript } from "./transcript.ts";

let merged = emptyTranscript;
merged = applyWireEvent(merged, {
  kind: "tool",
  callId: "call-1",
  tool: "city_get_demand",
  text: "{}",
  status: "Working",
});
merged = applyWireEvent(merged, {
  kind: "tool",
  callId: "call-1",
  text: "{\"limit\":16}",
  status: "Working",
});
merged = applyWireEvent(merged, {
  kind: "tool",
  callId: "call-1",
  text: "{\"limit\":16}",
  status: "Idle",
  result: "{\"residential\":1}",
  output: "{\"output\":true}",
});
assert.equal(merged.lines.length, 1);
const row = merged.lines[0];
assert.equal(row.kind, "tool");
if (row.kind === "tool") {
  assert.equal(row.name, "city_get_demand");
  assert.equal(row.args, "{\"limit\":16}");
  assert.equal(row.result, "{\"residential\":1}");
  assert.equal(row.output, "{\"output\":true}");
  assert.equal(row.state, "done");
}

let builtin = emptyTranscript;
builtin = applyWireEvent(builtin, {
  kind: "tool",
  tool: "place_building",
  text: "{\"prefab\":\"Clinic\"}",
  status: "Idle",
});
builtin = applyWireEvent(builtin, {
  kind: "tool",
  tool: "place_building",
  text: "placed",
  status: "Idle",
});
assert.equal(builtin.lines.length, 1);
const closed = builtin.lines[0];
assert.equal(closed.kind, "tool");
if (closed.kind === "tool") {
  assert.equal(closed.name, "place_building");
  assert.equal(closed.args, "{\"prefab\":\"Clinic\"}");
  assert.equal(closed.result, "placed");
  assert.equal(closed.state, "done");
}
