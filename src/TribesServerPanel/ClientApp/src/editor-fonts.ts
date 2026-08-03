// Monospace font choices for the file editor.
//
// "Fira Code" is bundled with the panel (public/fonts/firacode.woff2, SIL OFL — so
// redistribution is permitted) and is the default: it renders with no internet access,
// which matters for a server panel on a private network. Every other family is fetched
// from Google Fonts by the *browser* on demand, so those degrade to the local monospace
// stack if the machine is offline.

export const DEFAULT_EDITOR_FONT = "Fira Code";

// Shipped in the image, so it never needs a network fetch.
const BUNDLED = new Set<string>([DEFAULT_EDITOR_FONT]);

// Curated list, all verified to exist on Google Fonts. Note: "Anonymous Mono" is not a
// Google Fonts family — the (very similar) one that is published there is "Anonymous Pro".
export const EDITOR_FONTS: readonly string[] = [
  "Fira Code",
  "Cascadia Code",
  "Inconsolata",
  "Source Code Pro",
  "JetBrains Mono",
  "Roboto Mono",
  "Ubuntu Mono",
  "Nova Mono",
  "Syne Mono",
  "Libertinus Mono",
  "Iosevka Charon Mono",
  "Anonymous Pro",
  "Datatype",
  "VT323",
  "Reddit Mono",
];

// Google Fonts family names: letters, digits, single spaces and hyphens. Anything else is
// rejected rather than interpolated into a stylesheet URL or a CSS font-family value —
// this string reaches both, so it must never carry quotes, semicolons or angle brackets.
const FAMILY_RE = /^[A-Za-z0-9][A-Za-z0-9 -]{0,47}$/;

export function isValidFamily(family: string): boolean {
  return FAMILY_RE.test(family.trim());
}

const requested = new Set<string>();

/** Inject the Google Fonts stylesheet for `family` once (no-op for the bundled default). */
export function ensureFontLoaded(family: string): void {
  const f = (family || "").trim();
  if (!f || !isValidFamily(f) || BUNDLED.has(f) || requested.has(f)) return;
  requested.add(f);
  const link = document.createElement("link");
  link.rel = "stylesheet";
  // Google expects "+" for spaces; encodeURIComponent gives %20, so convert.
  link.href =
    "https://fonts.googleapis.com/css2?family=" +
    encodeURIComponent(f).replace(/%20/g, "+") +
    "&display=swap";
  document.head.appendChild(link);
}

/** CSS font-family stack: the chosen family, then the bundled default, then generics. */
export function fontStack(family: string | null | undefined): string {
  const f = family && isValidFamily(family) ? family.trim() : DEFAULT_EDITOR_FONT;
  return `"${f}", "${DEFAULT_EDITOR_FONT}", ui-monospace, Consolas, monospace`;
}
