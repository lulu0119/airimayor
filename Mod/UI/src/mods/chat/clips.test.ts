import assert from "node:assert/strict";
import { parseMessageClips, splitInlineRuns } from "./clips.ts";

let segments = parseMessageClips("Just words.");
assert.equal(segments.length, 1);
assert.equal(segments[0].kind, "text");

segments = parseMessageClips(
  'I placed <clip kind="building" index="18432" version="7">the school</clip> today.',
);
assert.equal(segments.length, 3);
assert.equal(segments[0].kind, "text");
assert.equal(segments[2].kind, "text");
const clip = segments[1];
assert.equal(clip.kind, "clip");
if (clip.kind === "clip") {
  assert.equal(clip.place.kind, "building");
  assert.equal(clip.place.name, "the school");
  assert.equal(clip.place.index, 18432);
  assert.equal(clip.place.version, 7);
}

segments = parseMessageClips('<clip kind="point" x="455" z="-70">here</clip>');
assert.equal(segments.length, 1);
const point = segments[0];
assert.equal(point.kind, "clip");
if (point.kind === "clip") {
  assert.equal(point.place.kind, "point");
  assert.equal(point.place.x, 455);
  assert.equal(point.place.z, -70);
}

for (const broken of [
  "An <clip>open tag.",
  '<clip kind="castle" index="1" version="2">nope</clip>',
  '<clip kind="building">no ids</clip>',
  '<clip kind="point" x="1">no z</clip>',
  '<clip kind="building" index="1" version="2">this label keeps going and going past forty characters</clip>',
]) {
  const parsed = parseMessageClips(broken);
  assert.ok(parsed.every((segment) => segment.kind === "text"));
  assert.equal(
    parsed.map((segment) => (segment.kind === "text" ? segment.text : "")).join(""),
    broken,
  );
}

const runs = splitInlineRuns("你指的 school\n\nnext");
assert.deepEqual(
  runs.map((run) => (run.kind === "text" ? run.text : "\n")),
  ["你", "指", "的", " ", "school", "\n", "\n", "next"],
);
