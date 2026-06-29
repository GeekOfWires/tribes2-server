// Self-hosted Monaco (no CDN, so it works in the offline container), with a faithful
// VS Code "Dark+" theme, a TorqueScript grammar (Tribes 2 .cs/.gui/.mis scripts), and
// an extension -> language map covering common Linux + config files.
import { loader } from "@monaco-editor/react";
import * as monaco from "monaco-editor";
import editorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import jsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import cssWorker from "monaco-editor/esm/vs/language/css/css.worker?worker";
import htmlWorker from "monaco-editor/esm/vs/language/html/html.worker?worker";
import tsWorker from "monaco-editor/esm/vs/language/typescript/ts.worker?worker";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
(self as any).MonacoEnvironment = {
  getWorker(_: unknown, label: string) {
    if (label === "json") return new jsonWorker();
    if (label === "css" || label === "scss" || label === "less") return new cssWorker();
    if (label === "html" || label === "handlebars" || label === "razor") return new htmlWorker();
    if (label === "typescript" || label === "javascript") return new tsWorker();
    return new editorWorker();
  },
};

loader.config({ monaco });

export const DARK_PLUS = "vscode-dark-plus";
let done = false;

export function setupMonaco(m: typeof monaco) {
  if (done) return;
  done = true;

  m.editor.defineTheme(DARK_PLUS, {
    base: "vs-dark",
    inherit: true,
    rules: [
      { token: "", foreground: "D4D4D4" },
      { token: "comment", foreground: "6A9955", fontStyle: "italic" },
      { token: "string", foreground: "CE9178" },
      { token: "string.escape", foreground: "D7BA7D" },
      { token: "keyword", foreground: "569CD6" },
      { token: "number", foreground: "B5CEA8" },
      { token: "number.hex", foreground: "B5CEA8" },
      { token: "number.float", foreground: "B5CEA8" },
      { token: "type", foreground: "4EC9B0" },
      { token: "type.identifier", foreground: "4EC9B0" },
      { token: "function", foreground: "DCDCAA" },
      { token: "variable", foreground: "9CDCFE" },
      { token: "variable.predefined", foreground: "9CDCFE" },
      { token: "identifier", foreground: "9CDCFE" },
      { token: "operator", foreground: "D4D4D4" },
      { token: "delimiter", foreground: "D4D4D4" },
      { token: "tag", foreground: "569CD6" },
      { token: "attribute.name", foreground: "9CDCFE" },
      { token: "attribute.value", foreground: "CE9178" },
      { token: "key", foreground: "9CDCFE" },
    ],
    colors: {
      "editor.background": "#1E1E1E",
      "editor.foreground": "#D4D4D4",
      "editorLineNumber.foreground": "#858585",
      "editorLineNumber.activeForeground": "#C6C6C6",
      "editor.selectionBackground": "#264F78",
      "editor.lineHighlightBackground": "#2A2A2A",
      "editorCursor.foreground": "#AEAFAD",
      "editorIndentGuide.background1": "#404040",
    },
  });

  // ---- TorqueScript (Tribes 2 engine scripting) -------------------------------
  m.languages.register({ id: "torquescript", extensions: [".cs", ".gui", ".mis", ".tscript"], aliases: ["TorqueScript", "Torque"] });
  m.languages.setLanguageConfiguration("torquescript", {
    comments: { lineComment: "//", blockComment: ["/*", "*/"] },
    brackets: [["{", "}"], ["[", "]"], ["(", ")"]],
    autoClosingPairs: [
      { open: "{", close: "}" }, { open: "[", close: "]" }, { open: "(", close: ")" },
      { open: '"', close: '"' }, { open: "'", close: "'" },
    ],
  });
  m.languages.setMonarchTokensProvider("torquescript", {
    defaultToken: "",
    keywords: [
      "function", "return", "if", "else", "while", "for", "switch", "switch$", "case",
      "default", "break", "continue", "datablock", "package", "new", "singleton",
      "parent", "true", "false", "or",
    ],
    // eslint-disable-next-line no-useless-escape
    symbols: /[=><!~?:&|+\-*\/\^%@]+/,
    tokenizer: {
      root: [
        [/\/\/.*$/, "comment"],
        [/\/\*/, "comment", "@comment"],
        [/[%$][A-Za-z_][\w:]*/, "variable"],          // %local and $global
        [/[A-Za-z_]\w*(?=\s*\()/, "function"],         // call sites
        [
          /[A-Za-z_]\w*/,
          { cases: { "@keywords": "keyword", "@default": "identifier" } },
        ],
        [/\d+\.\d+([eE][-+]?\d+)?/, "number.float"],
        [/0x[0-9a-fA-F]+/, "number.hex"],
        [/\d+/, "number"],
        [/"/, "string", "@string"],
        [/'/, "string", "@tstring"],
        [/[{}()[\]]/, "@brackets"],
        [/@symbols/, "operator"],
        [/[;,.]/, "delimiter"],
      ],
      comment: [
        [/[^*]+/, "comment"],
        [/\*\//, "comment", "@pop"],
        [/./, "comment"],
      ],
      string: [
        [/[^\\"]+/, "string"],
        [/\\./, "string.escape"],
        [/"/, "string", "@pop"],
      ],
      tstring: [
        [/[^\\']+/, "string"],
        [/\\./, "string.escape"],
        [/'/, "string", "@pop"],
      ],
    },
  });
}

const MAP: Record<string, string> = {
  cs: "torquescript", gui: "torquescript", mis: "torquescript", tscript: "torquescript",
  sh: "shell", bash: "shell", zsh: "shell",
  py: "python", json: "json", yml: "yaml", yaml: "yaml", xml: "xml", svg: "xml",
  ini: "ini", cfg: "ini", conf: "ini", toml: "ini", properties: "ini", env: "ini",
  md: "markdown", markdown: "markdown",
  js: "javascript", mjs: "javascript", cjs: "javascript", ts: "typescript",
  html: "html", htm: "html", css: "css", scss: "scss", less: "less",
  sql: "sql", c: "c", h: "c", cpp: "cpp", hpp: "cpp", go: "go", rs: "rust",
  rb: "ruby", php: "php", java: "java", pl: "perl", lua: "lua", bat: "bat", ps1: "powershell",
};

export function langFor(path: string): string {
  const name = path.split(/[\\/]/).pop() ?? "";
  if (/^dockerfile/i.test(name)) return "dockerfile";
  if (/^(makefile|gnumakefile)$/i.test(name)) return "plaintext";
  const ext = name.includes(".") ? name.split(".").pop()!.toLowerCase() : "";
  return MAP[ext] ?? "plaintext";
}
