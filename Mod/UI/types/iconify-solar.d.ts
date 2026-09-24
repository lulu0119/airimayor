// @iconify-icons/solar exposes per-icon subpaths via package.json "exports",
// which TypeScript 4.8 with moduleResolution "Node" cannot resolve. These
// ambient declarations cover the Solar icons used by the chat UI; webpack
// resolves the real modules at build time.
declare module "@iconify-icons/solar/plain-bold-duotone" {
  import type { IconifyIcon } from "@iconify/react";
  const icon: IconifyIcon;
  export default icon;
}

declare module "@iconify-icons/solar/stop-circle-bold-duotone" {
  import type { IconifyIcon } from "@iconify/react";
  const icon: IconifyIcon;
  export default icon;
}

declare module "@iconify-icons/solar/alt-arrow-down-bold-duotone" {
  import type { IconifyIcon } from "@iconify/react";
  const icon: IconifyIcon;
  export default icon;
}
