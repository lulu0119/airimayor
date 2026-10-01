import type { PointedPlace } from "./place";

// Inline place references in assistant text. A clip names a place the
// camera can visit: <clip kind="building" index="18432" version="7">the
// school</clip>. Malformed tags are not clips; the text stays as written
// so a bad tag can never break a message.
export type MessageSegment = { kind: "text"; text: string } | { kind: "clip"; place: PointedPlace };

// Gameface wraps flex items, not words inside a text node. A clip sitting in
// one text run is stretched to the height of that run, so each wrap unit is
// its own item: a word, one wide character, or a forced line break.
export type InlineRun = { kind: "text"; text: string } | { kind: "break" };

const wideCharacter = /[\u2e80-\u9fff\u3000-\u303f\u3040-\u30ff\uac00-\ud7af\uff00-\uffef]/;

export const splitInlineRuns = (text: string): InlineRun[] => {
  const normalized = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const runs: InlineRun[] = [];
  let word = "";
  const flush = () => {
    if (word.length === 0) {
      return;
    }
    runs.push({ kind: "text", text: word });
    word = "";
  };
  for (const character of normalized) {
    if (character === "\n") {
      flush();
      runs.push({ kind: "break" });
      continue;
    }
    if (character === " " || character === "\t") {
      word += character;
      flush();
      continue;
    }
    if (wideCharacter.test(character)) {
      flush();
      runs.push({ kind: "text", text: character });
      continue;
    }
    word += character;
  }
  flush();
  return runs;
};

const kinds = ["building", "road", "water", "sewage", "cable", "point"];
const tag = /<clip\s([^<>]*)>([^<>]*)<\/clip>/g;
const attr = /(\w+)="([^"]*)"/g;
// A clip label is a short name. Anything longer is a model slip (half the
// reply inside one tag); leave it as text instead of a giant card.
const maxLabel = 40;

const readNumber = (value: string | undefined, valid: (parsed: number) => boolean): number | null => {
  if (value === undefined || value.trim().length === 0) {
    return null;
  }
  const parsed = Number(value);
  return valid(parsed) ? parsed : null;
};

const readInt = (value: string | undefined): number | null =>
  readNumber(value, (parsed) => Number.isInteger(parsed));

const readFloat = (value: string | undefined): number | null =>
  readNumber(value, (parsed) => Number.isFinite(parsed));

export const parseMessageClips = (text: string): MessageSegment[] => {
  const segments: MessageSegment[] = [];
  let rest = 0;
  tag.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = tag.exec(text)) !== null) {
    if (match.index > rest) {
      segments.push({ kind: "text", text: text.slice(rest, match.index) });
    }
    rest = match.index + match[0].length;
    const place = readClip(match[1], match[2], segments.length);
    if (place === null) {
      segments.push({ kind: "text", text: match[0] });
    } else {
      segments.push({ kind: "clip", place });
    }
  }
  if (rest < text.length) {
    segments.push({ kind: "text", text: text.slice(rest) });
  }
  return segments;
};

const readClip = (attrs: string, label: string, key: number): PointedPlace | null => {
  const values: Record<string, string> = {};
  attr.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = attr.exec(attrs)) !== null) {
    values[match[1]] = match[2];
  }
  const kind = values.kind ?? "";
  if (!kinds.includes(kind)) {
    return null;
  }
  const name = label.trim();
  if (name.length > maxLabel) {
    return null;
  }
  const title = name.length > 0 ? name : "Place";
  if (kind === "point") {
    const x = readFloat(values.x);
    const z = readFloat(values.z);
    if (x === null || z === null) {
      return null;
    }
    return { id: `clip-${key}`, kind, name: title, x, z, index: 0, version: 0 };
  }
  const index = readInt(values.index);
  const version = readInt(values.version);
  if (index === null || version === null) {
    return null;
  }
  const x = readFloat(values.x) ?? 0;
  const z = readFloat(values.z) ?? 0;
  const endX = readFloat(values.endX);
  const endZ = readFloat(values.endZ);
  return {
    id: `clip-${key}`,
    kind,
    name: title,
    x,
    z,
    index,
    version,
    endX: endX ?? undefined,
    endZ: endZ ?? undefined,
  };
};
