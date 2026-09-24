export function jpegPixelSize(dataUri: string): { width: number; height: number } | null {
  const comma = dataUri.indexOf(",");
  if (comma < 0 || !dataUri.slice(0, comma).includes("base64")) {
    return null;
  }
  const bytes = decodeBase64(dataUri.slice(comma + 1));
  if (!bytes || bytes.length < 4 || bytes[0] !== 0xff || bytes[1] !== 0xd8) {
    return null;
  }
  let index = 2;
  while (index + 1 < bytes.length) {
    if (bytes[index] !== 0xff) {
      return null;
    }
    while (index < bytes.length && bytes[index] === 0xff) {
      index++;
    }
    if (index >= bytes.length) {
      return null;
    }
    const marker = bytes[index++];
    if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd9)) {
      continue;
    }
    if (index + 1 >= bytes.length) {
      return null;
    }
    const length = (bytes[index] << 8) | bytes[index + 1];
    if (length < 2 || index + length > bytes.length) {
      return null;
    }
    if (marker >= 0xc0 && marker <= 0xc2 && length >= 7) {
      const height = (bytes[index + 3] << 8) | bytes[index + 4];
      const width = (bytes[index + 5] << 8) | bytes[index + 6];
      if (width > 0 && height > 0) {
        return { width, height };
      }
      return null;
    }
    index += length;
  }
  return null;
}

function decodeBase64(text: string): Uint8Array | null {
  const out = new Uint8Array(Math.floor((text.length * 3) / 4));
  let written = 0;
  let buffer = 0;
  let bits = 0;
  for (let index = 0; index < text.length; index++) {
    const value = base64Value(text.charCodeAt(index));
    if (value < 0) {
      if (text[index] === "=") {
        break;
      }
      return null;
    }
    buffer = (buffer << 6) | value;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      out[written++] = (buffer >> bits) & 0xff;
    }
  }
  return out.subarray(0, written);
}

function base64Value(code: number): number {
  if (code >= 65 && code <= 90) {
    return code - 65;
  }
  if (code >= 97 && code <= 122) {
    return code - 71;
  }
  if (code >= 48 && code <= 57) {
    return code + 4;
  }
  if (code === 43) {
    return 62;
  }
  if (code === 47) {
    return 63;
  }
  return -1;
}
