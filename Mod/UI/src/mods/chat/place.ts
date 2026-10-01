export interface PointedPlace {
  id: string;
  kind: string;
  name: string;
  x: number;
  z: number;
  index: number;
  version: number;
  endX?: number;
  endZ?: number;
}

const readNumber = (value: unknown): number | null =>
  typeof value === "number" && Number.isFinite(value) ? value : null;

export const readPlaces = (value: unknown): PointedPlace[] => {
  const source = typeof value === "string" ? parseJson(value) : value;
  if (!Array.isArray(source)) {
    return [];
  }
  const places: PointedPlace[] = [];
  for (const item of source) {
    if (item == null || typeof item !== "object") {
      continue;
    }
    const record = item as Record<string, unknown>;
    const x = readNumber(record.x);
    const z = readNumber(record.z);
    if (typeof record.kind !== "string" || typeof record.name !== "string" || x == null || z == null) {
      continue;
    }
    const endX = readNumber(record.endX);
    const endZ = readNumber(record.endZ);
    places.push({
      id: typeof record.id === "string" ? record.id : "",
      kind: record.kind,
      name: record.name,
      x,
      z,
      index: readNumber(record.index) ?? 0,
      version: readNumber(record.version) ?? 0,
      endX: endX ?? undefined,
      endZ: endZ ?? undefined,
    });
  }
  return places;
};

const parseJson = (json: string): unknown => {
  try {
    return JSON.parse(json);
  } catch {
    return null;
  }
};
