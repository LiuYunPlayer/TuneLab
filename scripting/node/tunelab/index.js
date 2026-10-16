// TuneLab scripted-plugin facade (Node.js, zero-dependency).
//
// Speaks the sidecar wire protocol (spec: scripting/wire/v1.json): LSP-style
// Content-Length frames of JSON-RPC 2.0 over stdio. Covers format / effect / voice
// entries on the inline plane (shm is a later fast lane).
//
// Usage (package entry, e.g. plugin/main.js — CommonJS):
//
//   const { format, serve } = require("tunelab");
//   format({
//     importProject(path) { return { tracks: [], tempos: [], timeSignatures: [] }; },
//     exportProject(path, project) { ... },
//   });
//   serve();
"use strict";

const fs = require("fs");
const crypto = require("crypto");

const WIRE_PROTOCOL = 1;
const VERSION = "0.1.0";

// ---------------------------------------------------------------- property wire
// Tagged values (host readers accept tagged and plain alike; we always emit tagged
// inside "properties" maps): {b}|{n}(number|"NaN"|"Infinity"|"-Infinity")|{s}|{o}|{a}|null.

function encodeValue(v) {
  if (v === null || v === undefined) return null;
  const t = typeof v;
  if (t === "boolean") return { b: v };
  if (t === "number") return { n: specialStr(v) || v };
  if (t === "string") return { s: v };
  if (Array.isArray(v)) return { a: v.map(encodeValue) };
  if (t === "object") return { o: encodeMap(v) };
  throw new TypeError("cannot encode property value of type " + t);
}

function encodeMap(m) {
  const out = {};
  for (const [k, v] of Object.entries(m)) out[k] = encodeValue(v);
  return out;
}

function decodeValue(v) {
  if (v === null) return null;
  if (typeof v === "boolean" || typeof v === "string") return v;
  if (typeof v === "number") return v;
  if (Array.isArray(v)) return v.map(decodeValue);
  if (typeof v === "object") {
    const keys = Object.keys(v);
    if (keys.length === 1) {
      const tag = keys[0], p = v[tag];
      if (tag === "b" || tag === "s") return p;
      if (tag === "n") return typeof p === "string" ? Number(p === "Infinity" ? Infinity : p === "-Infinity" ? -Infinity : NaN) : p;
      if (tag === "o") return decodeMap(p);
      if (tag === "a") return p.map(decodeValue);
    }
    return decodeMap(v);
  }
  return v;
}

function decodeMap(m) {
  const out = {};
  for (const [k, v] of Object.entries(m || {})) out[k] = decodeValue(v);
  return out;
}

function specialStr(v) {
  if (Number.isNaN(v)) return "NaN";
  if (v === Infinity) return "Infinity";
  if (v === -Infinity) return "-Infinity";
  return null;
}

// Anywhere in an outgoing payload, bare special floats would stringify to `null` —
// convert them to their wire string literals (never emit bare NaN tokens: illegal JSON).
function sanitize(node) {
  if (Array.isArray(node)) return node.map(sanitize);
  if (node && typeof node === "object") {
    const out = {};
    for (const [k, v] of Object.entries(node)) out[k] = sanitize(v);
    return out;
  }
  if (typeof node === "number") return specialStr(node) || node;
  return node;
}

function walkEncode(node) {
  if (Array.isArray(node)) return node.map(walkEncode);
  if (node && typeof node === "object") {
    const out = {};
    for (const [k, v] of Object.entries(node))
      out[k] = k === "properties" && v && typeof v === "object" && !Array.isArray(v) ? encodeMap(v) : walkEncode(v);
    return out;
  }
  return node;
}

function walkDecode(node) {
  if (Array.isArray(node)) return node.map(walkDecode);
  if (node && typeof node === "object") {
    const out = {};
    for (const [k, v] of Object.entries(node))
      out[k] = k === "properties" && v && typeof v === "object" && !Array.isArray(v) ? decodeMap(v) : walkDecode(v);
    return out;
  }
  return node;
}

// ---------------------------------------------------------------- binary audio helpers
const b64ToF32 = (s) => { const buf = Buffer.from(s, "base64"); const out = new Float32Array(buf.length / 4); for (let i = 0; i < out.length; i++) out[i] = buf.readFloatLE(i * 4); return out; };
const f32ToB64 = (a) => { const buf = Buffer.alloc(a.length * 4); for (let i = 0; i < a.length; i++) buf.writeFloatLE(a[i], i * 4); return buf.toString("base64"); };
const b64ToF64 = (s) => { const buf = Buffer.from(s, "base64"); const out = new Float64Array(buf.length / 8); for (let i = 0; i < out.length; i++) out[i] = buf.readDoubleLE(i * 8); return out; };
const f64ToB64 = (a) => { const buf = Buffer.alloc(a.length * 8); for (let i = 0; i < a.length; i++) buf.writeDoubleLE(a[i], i * 8); return buf.toString("base64"); };

// ---------------------------------------------------------------- service
const formatEntries = new Map();     // entry id -> {importProject, exportProject, getSettingsConfig?, applySettings?}
const effectEngines = new Map();     // engine id -> object
const voiceEngines = new Map();      // engine id -> object
const pullSessions = new Map();      // session key -> {kind, ctx, user}
const defaultEntry = () => (formatEntries.size === 1 ? [...formatEntries.keys()][0] : null);

let stdinBuf = Buffer.alloc(0);
let stdoutLock = false;
const pendingHostCalls = new Map(); // id -> {resolve, reject}
let hostCallSeq = 0;

function writeMessage(obj) {
  const body = Buffer.from(JSON.stringify(obj), "utf8");
  const frame = Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "ascii"), body]);
  // single pending writer: interleaving would corrupt framing
  const drain = () => fs.writeSync(1, frame);   // sync write to fd 1: never interleaves
  drain();
}

function reply(id, result) { writeMessage({ jsonrpc: "2.0", id, result: result ?? {} }); }
function replyErr(id, code, message) { writeMessage({ jsonrpc: "2.0", id, error: { code, message } }); }

function callHost(method, params) {
  return new Promise((resolve, reject) => {
    const id = "n" + ++hostCallSeq;
    pendingHostCalls.set(id, { resolve, reject });
    writeMessage({ jsonrpc: "2.0", id, method, params });
  });
}

function notify(method, params) { writeMessage({ jsonrpc: "2.0", method, params }); }

// ---- output segments (shared by effect/voice ctx) ----
class OutputSegment {
  constructor(ctxRef, id, count) { this.ctx = ctxRef; this.id = id; this.count = count; this.closed = false; }
  async write(offset, samples) {
    const arr = samples instanceof Float32Array ? samples : Float32Array.from(samples);
    await callHost("host.segment_write", this.ctx._via({ segment: this.id, offset, data: f32ToB64(arr) }));
  }
  async commit() { await callHost("host.segment_commit", this.ctx._via({ segment: this.id })); }
  async resize(sampleOffset, sampleCount) {
    this.count = sampleCount;
    await callHost("host.segment_resize", this.ctx._via({ segment: this.id, sample_offset: sampleOffset, sample_count: sampleCount }));
  }
  async dispose() {
    if (this.closed) return;
    this.closed = true;
    await callHost("host.segment_dispose", this.ctx._via({ segment: this.id }));
  }
}

class PullCtx {
  constructor(engine, key) { this.engine = engine; this.session = key; }
  _via(extra) { return { engine: this.engine, session: this.session, ...(extra || {}) }; }
  async properties(keys) { return decodeMap((await callHost("host.properties", this._via({ keys: keys || null }))).values); }
  async automationTracks() { return (await callHost("host.automation_tracks", this._via())).tracks; }
  async evaluate(track, positions) {
    const pos = positions instanceof Float64Array ? positions : Float64Array.from(positions);
    const r = await callHost("host.evaluate", this._via({ track, positions: f64ToB64(pos) }));
    return b64ToF64(r.values);
  }
  async output(sampleOffset, sampleCount, sampleRate) {
    const r = await callHost("host.segment_create", this._via({ sample_offset: sampleOffset, sample_count: sampleCount, sample_rate: sampleRate }));
    return new OutputSegment(this, r.segment, sampleCount);
  }
  status(segments) { notify("effect.status", this._via({ segments: segments.map(statusDict) })); }
  parameters(map) { notify("effect.parameters", this._via({ parameters: map })); }
}

class EffectCtx extends PullCtx {
  constructor(engine, key) { super(engine, key); this.segments = []; this.cancelled = false; }
  async inputGeometry() { const r = await callHost("host.input_read", this._via({ offset: 0, count: 0 })); return r; }
  async inputRead(offset = 0, count = null) {
    const r = await callHost("host.input_read", this._via({ offset, count: count ?? undefined }));
    return { data: b64ToF32(r.data), sampleRate: r.sample_rate };
  }
  async outputSegment(sampleOffset, sampleCount, sampleRate) { const s = await this.output(sampleOffset, sampleCount, sampleRate); this.segments.push(s); return s; }
}

class Evaluator {
  constructor(ctx, track) { this.ctx = ctx; this.track = track; }
  evaluate(positions) { return this.ctx.evaluate(this.track, positions); }
}

class VoiceCtx extends PullCtx {
  constructor(engine, key, voiceId) { super(engine, key); this.voiceId = voiceId; this.segments = []; this.cancelled = false; }
  async notes() { return (await callHost("host.notes", this._via())).notes.map(noteFromWire); }
  async snapshot(noteIds) {
    const ids = noteIds.map((n) => (typeof n === "string" ? n : n.id));
    const d = (await callHost("host.snapshot", this._via({ note_ids: ids }))).snapshot;
    return {
      notes: d.notes.map(noteFromWire),
      partProperties: decodeMap(d.part_properties),
      pitch: new Evaluator(this, "pitch"),
      pitchDeviation: new Evaluator(this, "pitch_deviation"),
      automations: Object.fromEntries((d.automations || []).map((t) => [t, new Evaluator(this, t)])),
    };
  }
  async outputSegment(sampleOffset, sampleCount, sampleRate) { const s = await this.output(sampleOffset, sampleCount, sampleRate); this.segments.push(s); return s; }
  statusPublished(segments) { notify("voice.status", this._via({ segments: segments.map(statusDict) })); }
  pitch(segments) { notify("voice.pitch", this._via({ segments: segments.map((seg) => seg.map((p) => [p[0], p[1]])) })); }
  phonemes(byNote) {
    const out = {};
    for (const [id, s] of Object.entries(byNote)) out[id] = { leading: s[0], body: s[1], body_offset: s.length > 2 ? s[2] : 0 };
    notify("voice.phonemes", this._via({ syllables: out }));
  }
}

function noteFromWire(d) {
  const ph = (p) => ({ symbol: p.symbol, duration: p.duration ?? 0, stretchWeight: p.stretch_weight ?? 0, properties: decodeMap(p.properties || {}) });
  return {
    id: d.id ?? d.request_id ?? "", start: d.start, end: d.end, pitch: d.pitch, lyric: d.lyric,
    bodyOffset: d.body_offset ?? 0,
    leading: (d.leading || []).map(ph), body: (d.body || []).map(ph),
    properties: decodeMap(d.properties || {}),
  };
}

function statusDict(s) {
  return { start: s[0], end: s[1], status: s[2], progress: s.length > 3 ? s[3] : 0, message: s.length > 4 ? s[4] : null };
}

// ---- inbound dispatch ----
async function handleRequest(msg) {
  const { id, method, params = {} } = msg;
  try {
    switch (method) {
      case "initialize": return reply(id, onInitialize(params));

      case "ping": return reply(id, {});

      case "shutdown": reply(id, {}); process.exit(0); return undefined;

      case "format.import": {
        const h = formatEntries.get(params.entry) || (defaultEntry() && formatEntries.get(defaultEntry()));
        if (!h || !h.importProject) throw new Error("no importer for entry " + params.entry);
        const project = await h.importProject(params.stream_ref);
        return reply(id, { project: walkEncode(project) });
      }
      case "format.export": {
        const h = formatEntries.get(params.entry) || (defaultEntry() && formatEntries.get(defaultEntry()));
        if (!h || !h.exportProject) throw new Error("no exporter for entry " + params.entry);
        await h.exportProject(params.stream_ref, walkDecode(params.project || {}));
        return reply(id, {});
      }
      case "settings.get_config": {
        const h = formatEntries.get(params.entry);
        const fn = h && h.getSettingsConfig;
        if (!fn) throw new Error("no settings for entry " + params.entry);
        return reply(id, { config: sanitize(fn()) });
      }
      case "settings.apply": {
        const h = formatEntries.get(params.entry) || (defaultEntry() && formatEntries.get(defaultEntry()));
        if (h && h.applySettings) await h.applySettings(decodeMap(params.values || {}));
        return id != null ? reply(id, {}) : undefined;
      }

      case "engine.init": {
        const obj = effectEngines.get(params.engine) || voiceEngines.get(params.engine);
        if (!obj) throw new Error("no such engine " + params.engine);
        if (obj.init) await obj.init();
        return reply(id, {});
      }
      case "engine.destroy": {
        const obj = effectEngines.get(params.engine) || voiceEngines.get(params.engine);
        try { if (obj && obj.destroy) await obj.destroy(); } catch { /* best effort */ }
        return id != null ? reply(id, {}) : undefined;
      }
      case "engine.create_session": {
        const key = String(params.session || "");
        if (key.startsWith("voice:")) {
          const eng = voiceEngines.get(params.engine);
          const ctx = new VoiceCtx(params.engine, key, params.voice_id || "");
          const user = eng.create_session ? await eng.create_session(ctx) : eng;
          ctx.user = user; pullSessions.set(key, { kind: "voice", ctx, user });
        } else {
          const eng = effectEngines.get(params.engine);
          const ctx = new EffectCtx(params.engine, key);
          const user = eng.create_session ? await eng.create_session(ctx) : eng;
          ctx.user = user; pullSessions.set(key, { kind: "effect", ctx, user });
        }
        return reply(id, {});
      }
      case "session.dispose": {
        const s = pullSessions.get(String(params.session || ""));
        pullSessions.delete(String(params.session || ""));
        if (s) {
          for (const seg of s.ctx.segments) { try { await seg.dispose(); } catch { /* gone */ } }
          try { if (s.user && s.user.dispose) await s.user.dispose(); } catch { /* best effort */ }
        }
        return id != null ? reply(id, {}) : undefined;
      }
      case "effect.cancel": case "voice.cancel": {
        const s = pullSessions.get(String(params.session || ""));
        if (s) { s.ctx.cancelled = true; if (s.user && s.user.cancel) s.user.cancel(); }
        return id != null ? reply(id, {}) : undefined;
      }
      case "voice.dirty": {
        const s = pullSessions.get(String(params.session || ""));
        const hook = s && s.user && (s.user.onDirty || s.user.on_dirty);
        if (hook) { try { hook.call(s.user, params.kind, params); } catch { /* a coarse signal must never kill the loop */ } }
        return undefined;
      }

      case "effect.process": {
        const s = requireSession(params.session);
        if (s.user.process) await s.user.process(s.ctx);
        return reply(id, {});
      }
      case "effect.get_property_config": {
        const eng = requireEngine(effectEngines, params.engine);
        return reply(id, { config: sanitize(eng.getPropertyConfig ? eng.getPropertyConfig(params.effects || []) : { type: "object", properties: [] }) });
      }
      case "effect.get_automation_configs": {
        const eng = requireEngine(effectEngines, params.engine);
        return reply(id, { tracks: sanitize(eng.getAutomationConfigs ? eng.getAutomationConfigs(params.effects || []) : []) });
      }
      case "effect.get_synthesized_parameter_configs": {
        const eng = requireEngine(effectEngines, params.engine);
        return reply(id, { tracks: sanitize(eng.getSynthesizedParameterConfigs ? eng.getSynthesizedParameterConfigs(params.effects || []) : []) });
      }

      case "voice.get_catalog": {
        const eng = requireEngine(voiceEngines, params.engine);
        const [sources, layout] = normalizeCatalog(await eng.catalog());
        return reply(id, { sources, layout });
      }
      case "voice.get_automation_configs":
        return reply(id, { tracks: sanitize((requireEngine(voiceEngines, params.engine).automationConfigs || (() => []))(params.parts || [])) });
      case "voice.get_synthesized_parameter_configs":
        return reply(id, { tracks: sanitize((requireEngine(voiceEngines, params.engine).synthesizedParameterConfigs || (() => []))(params.parts || [])) });
      case "voice.get_part_property_config":
        return reply(id, { config: sanitize(requireEngine(voiceEngines, params.engine).partPropertyConfig(params.parts || [])) });
      case "voice.get_note_property_config":
        return reply(id, { config: sanitize(requireEngine(voiceEngines, params.engine).notePropertyConfig(params.part || {}, params.notes || [])) });
      case "voice.get_phoneme_property_configs":
        return reply(id, { slots: sanitize((requireEngine(voiceEngines, params.engine).phonemePropertyConfigs || (() => []))(params.part || {}, params.notes || [])) });
      case "voice.default_lyric":
        return reply(id, { lyric: pullSessions.get(params.session).user.defaultLyric ?? "la" });
      case "voice.is_continuation":
        return reply(id, { continuation: !!(await pullSessions.get(params.session).user.isContinuation(noteFromWire(params.note))) });
      case "voice.peek": {
        const r = await pullSessions.get(params.session).user.peek(params.start, params.end);
        return reply(id, { range: r ? [r[0], r[1]] : null });
      }
      case "voice.synthesize": {
        const s = requireSession(params.session);
        await s.user.synthesize(params.start, params.end);
        return reply(id, {});
      }

      case "host.input_read": case "host.properties": case "host.evaluate":
      case "host.automation_tracks": case "host.snapshot": case "host.notes":
      case "host.segment_create": case "host.segment_write": case "host.segment_commit":
      case "host.segment_resize": case "host.segment_dispose":
        return undefined; // responses arrive as replies (handled below), never as requests

      default:
        if (id != null) replyErr(id, "bad_request", "unknown method " + method);
        return undefined;
    }
  } catch (ex) {
    if (id != null) replyErr(id, "runtime_error", String(ex && ex.message ? ex.message : ex));
    else console.error("notification failed:", ex);
    return undefined;
  }
}

function requireSession(key) {
  const s = pullSessions.get(String(key));
  if (!s) throw new Error("unknown session " + key);
  return s;
}
function requireEngine(map, id) {
  const eng = map.get(id);
  if (!eng) throw new Error("no such engine " + id);
  return eng;
}
function normalizeCatalog(r) {
  if (Array.isArray(r) && r.length === 2 && Array.isArray(r[0])) return [r[0], r[1] || []];
  return [Array.isArray(r) ? r : [], []];
}

function onInitialize(params) {
  const provides = [];
  for (const e of params.entries || []) {
    if (e.type === "format") {
      let h = formatEntries.get(e.entry);
      if (!h && formatEntries.size === 1) { h = [...formatEntries.values()][0]; formatEntries.set(e.entry, h); }
      if (!h) continue;
      provides.push({ type: "format", entry: e.entry, can_import: !!h.importProject, can_export: !!h.exportProject, settings: !!h.getSettingsConfig });
      if (e.settings && h.applySettings) { try { h.applySettings(decodeMap(e.settings)); } catch { /* user code threw */ } }
    } else {
      const known = effectEngines.has(e.engine) || voiceEngines.has(e.engine);
      if (!known) continue;
      const eng = effectEngines.get(e.engine) || voiceEngines.get(e.engine);
      provides.push({ type: e.type, engine: e.engine, settings: !!eng.getSettingsConfig });
      if (e.settings && eng.applySettings) { try { eng.applySettings(decodeMap(e.settings)); } catch { /* ignore */ } }
    }
  }
  return {
    provides,
    capabilities: { audio_plane: "inline", curve_plane: "inline" },
    runtime: { exe: process.execPath, version: process.versions.node, arch: process.arch, flavor: "node" },
    facade_version: VERSION,
  };
}

function onStdinChunk(chunk) {
  stdinBuf = Buffer.concat([stdinBuf, chunk]);
  for (;;) {
    const headerEnd = stdinBuf.indexOf("\r\n\r\n");
    if (headerEnd < 0) return;
    const headers = stdinBuf.subarray(0, headerEnd).toString("ascii");
    const m = /content-length:\s*(\d+)/i.exec(headers);
    if (!m) { stdinBuf = stdinBuf.subarray(headerEnd + 4); continue; }
    const len = parseInt(m[1], 10);
    if (stdinBuf.length < headerEnd + 4 + len) return;
    const body = stdinBuf.subarray(headerEnd + 4, headerEnd + 4 + len).toString("utf8");
    stdinBuf = stdinBuf.subarray(headerEnd + 4 + len);
    let msg;
    try { msg = JSON.parse(body); } catch (ex) { process.exit(1); }   // broken frame = protocol violation
    if (msg && msg.method == null && msg.id != null) {
      const p = pendingHostCalls.get(String(msg.id));
      if (p) {
        pendingHostCalls.delete(String(msg.id));
        if (msg.error) p.reject(new Error(`${msg.error.code}: ${msg.error.message}`));
        else p.resolve(msg.result || {});
      }
      continue;
    }
    if (msg && msg.method) {
      handleRequest(msg).catch((ex) => console.error("dispatch error:", ex));
    }
  }
}

function serve() {
  // parent-death guard (defense line 2 cross-platform half): reparenting = host gone.
  const parent = process.ppid;
  const t = setInterval(() => { if (process.ppid !== parent) process.exit(0); }, 2000);
  t.unref();
  process.stdin.on("data", onStdinChunk);
  process.stdin.on("end", () => process.exit(0));   // defense line 1: stdin EOF
  process.stdin.resume();
}

// ---------------------------------------------------------------- registration
function format(handlers) { formatEntries.set(handlers.entry || `auto-${formatEntries.size}`, handlers); return handlers; }
function effect(engineId, obj) { effectEngines.set(engineId, obj); return obj; }
function voice(engineId, obj) { voiceEngines.set(engineId, obj); return obj; }

module.exports = {
  format, effect, voice, serve,
  info: (msg) => notify("log.write", { level: "info", message: String(msg) }),
  warn: (msg) => notify("log.write", { level: "warning", message: String(msg) }),
  error: (msg) => notify("log.write", { level: "error", message: String(msg) }),
  capabilities: () => ({ audio_plane: "inline", curve_plane: "inline" }),
  encodeValue, decodeValue, encodeMap, decodeMap, walkEncode, walkDecode, sanitize,
  f32ToB64, b64ToF32, f64ToB64, b64ToF64,
  WIRE_PROTOCOL, VERSION,
};
