import { useCallback, useEffect, useRef, useState } from "react";
import Editor from "@monaco-editor/react";
import { api, type DirListing, type FileRead } from "../api";
import { DARK_PLUS, langFor, setupMonaco } from "../monaco-setup";

const join = (dir: string, name: string) => dir.replace(/\/+$/, "") + "/" + name;

export default function Files() {
  const [listing, setListing] = useState<DirListing | null>(null);
  const [sel, setSel] = useState<string | null>(null);
  const [read, setRead] = useState<FileRead | null>(null);
  const [content, setContent] = useState("");
  const [orig, setOrig] = useState("");
  const [msg, setMsg] = useState<{ ok: boolean; t: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const dirty = read?.content !== undefined && content !== orig;

  const loadDir = useCallback(async (path?: string) => {
    try { setListing(await api.listDir(path)); }
    catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
  }, []);
  useEffect(() => { loadDir(); }, [loadDir]);

  const openFile = async (path: string) => {
    if (dirty && !window.confirm("Discard unsaved changes?")) return;
    setSel(path); setMsg(null);
    try {
      const r = await api.readFile(path);
      setRead(r);
      setContent(r.content ?? "");
      setOrig(r.content ?? "");
    } catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
  };

  const save = async () => {
    if (!sel) return;
    setBusy(true);
    try { await api.saveFile(sel, content); setOrig(content); setMsg({ ok: true, t: "Saved (audited)." }); }
    catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
    finally { setBusy(false); }
  };

  const create = async (isDir: boolean) => {
    if (!listing) return;
    const name = window.prompt(`New ${isDir ? "folder" : "file"} name:`);
    if (!name) return;
    try { await api.createPath(join(listing.path, name), isDir); await loadDir(listing.path); }
    catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
  };

  const upload = async (files: FileList | null) => {
    if (!files || !files.length || !listing) return;
    setBusy(true);
    try {
      const r = await api.uploadFiles(listing.path, files);
      setMsg({ ok: true, t: `Uploaded ${r.uploaded.length} file(s) (audited).` });
      await loadDir(listing.path);
    } catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
    finally { setBusy(false); if (fileInput.current) fileInput.current.value = ""; }
  };

  const del = async (path: string, isDir: boolean) => {
    if (!window.confirm(`Delete ${isDir ? "folder" : "file"} "${path}"? (audited; root can revert)`)) return;
    try {
      await api.deletePath(path);
      if (sel === path) { setSel(null); setRead(null); setContent(""); setOrig(""); }
      await loadDir(listing?.path);
      setMsg({ ok: true, t: "Deleted (audited)." });
    } catch (e) { setMsg({ ok: false, t: (e as Error).message }); }
  };

  return (
    <div>
      <h2>Files</h2>
      <p className="muted" style={{ marginTop: -6 }}>
        Edits are scoped (Developers: under GameData; root: anywhere) and every change is audited for revert.
      </p>
      <div className="files-grid">
        {/* browser */}
        <div className="card files-browser">
          <div className="row" style={{ justifyContent: "space-between" }}>
            <strong style={{ wordBreak: "break-all" }}>{listing?.path ?? "…"}</strong>
          </div>
          <div className="row" style={{ margin: "8px 0" }}>
            <button className="btn" disabled={!listing?.parent} onClick={() => listing?.parent && loadDir(listing.parent)}>↑ Up</button>
            <button className="btn" onClick={() => loadDir(listing?.path)}>↻</button>
            <button className="btn" onClick={() => create(false)}>+ File</button>
            <button className="btn" onClick={() => create(true)}>+ Folder</button>
            <button className="btn" disabled={busy} onClick={() => fileInput.current?.click()}>↥ Upload</button>
            <input ref={fileInput} type="file" multiple style={{ display: "none" }} onChange={(e) => upload(e.target.files)} />
          </div>
          <div className="filelist">
            {listing?.entries.length === 0 && <div className="muted" style={{ padding: 8 }}>empty</div>}
            {listing?.entries.map((e) => {
              const full = join(listing.path, e.name);
              return (
                <div key={e.name} className={"filerow" + (sel === full ? " active" : "")}>
                  <button className="filename" onClick={() => (e.isDir ? loadDir(full) : openFile(full))}>
                    {e.isDir ? "📁" : "📄"} {e.name}
                  </button>
                  <button className="filedel" title="delete" onClick={() => del(full, e.isDir)}>✕</button>
                </div>
              );
            })}
          </div>
        </div>

        {/* editor */}
        <div className="card files-editor">
          {!sel && <div className="muted" style={{ padding: 12 }}>Select a file to edit.</div>}
          {sel && read?.isBinary && <div className="err" style={{ padding: 12 }}>Binary file — not editable here.</div>}
          {sel && read?.tooLarge && <div className="err" style={{ padding: 12 }}>File too large to edit ({read.size} bytes).</div>}
          {sel && read?.content !== undefined && (
            <>
              <div className="row" style={{ justifyContent: "space-between", marginBottom: 8 }}>
                <span style={{ wordBreak: "break-all" }}>{sel}{dirty ? " *" : ""} <span className="muted">({langFor(sel)})</span></span>
                <span className="row">
                  <button className="btn primary" disabled={busy || !dirty} onClick={save}>Save</button>
                  <button className="btn danger" onClick={() => del(sel, false)}>Delete</button>
                </span>
              </div>
              <Editor
                height="62vh"
                theme={DARK_PLUS}
                path={sel}
                language={langFor(sel)}
                value={content}
                onChange={(v) => setContent(v ?? "")}
                beforeMount={setupMonaco}
                options={{ fontSize: 13, fontFamily: '"Share Tech Mono", ui-monospace, monospace', minimap: { enabled: false }, tabSize: 3, renderWhitespace: "selection", scrollBeyondLastLine: false }}
              />
            </>
          )}
        </div>
      </div>
      {msg && <p className={msg.ok ? "ok" : "err"}>{msg.t}</p>}
    </div>
  );
}
