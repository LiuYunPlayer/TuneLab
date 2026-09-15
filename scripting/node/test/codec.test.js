"use strict";
const test = require("node:test");
const assert = require("node:assert");
const {
  encodeValue, decodeValue, encodeMap, decodeMap,
  walkEncode, walkDecode, sanitize,
  f32ToB64, b64ToF32, f64ToB64, b64ToF64,
} = require("../tunelab/index.js");

test("property tagged round-trip", () => {
  const native = { s: "x", n: 0.5, b: true, nothing: null, arr: [1, "a"], obj: { inner: -2.25 } };
  const tagged = encodeMap(native);
  assert.deepStrictEqual(tagged.s, { s: "x" });
  assert.deepStrictEqual(tagged.n, { n: 0.5 });
  assert.deepStrictEqual(tagged.b, { b: true });
  assert.strictEqual(tagged.nothing, null);
  assert.deepStrictEqual(tagged.obj, { o: { inner: { n: -2.25 } } });
  const back = decodeMap(tagged);
  assert.strictEqual(back.s, "x");
  assert.strictEqual(back.n, 0.5);
  assert.strictEqual(back.b, true);
  assert.deepStrictEqual(back.arr, [1, "a"]);
  assert.deepStrictEqual(back.obj, { inner: -2.25 });
});

test("special floats ride string literals, never bare", () => {
  assert.deepStrictEqual(encodeValue(NaN), { n: "NaN" });
  assert.deepStrictEqual(encodeValue(Infinity), { n: "Infinity" });
  assert.deepStrictEqual(encodeValue(-Infinity), { n: "-Infinity" });
  assert(Number.isNaN(decodeValue({ n: "NaN" })));
  assert.strictEqual(decodeValue({ n: "Infinity" }), Infinity);
  // sanitize catches specials anywhere; JSON.stringify would otherwise emit `null`
  const payload = sanitize({ defaultValue: NaN, list: [1, Infinity], text: "NaN stays a string" });
  assert.strictEqual(payload.defaultValue, "NaN");
  assert.strictEqual(payload.list[1], "Infinity");
  assert.strictEqual(JSON.parse(JSON.stringify(payload)).defaultValue, "NaN");
  assert.strictEqual(payload.text, "NaN stays a string");   // plain strings untouched
});

test("project walker only encodes properties maps", () => {
  const project = { tracks: [{ parts: [{ notes: [{ pos: 0, dur: 1, pitch: 60, properties: { tension: 0.5 } }] }] }] };
  const out = walkEncode(project);
  assert.deepStrictEqual(out.tracks[0].parts[0].notes[0].properties, { tension: { n: 0.5 } });
  assert.strictEqual(out.tracks[0].parts[0].notes[0].pitch, 60);
  assert.deepStrictEqual(walkDecode(out).tracks[0].parts[0].notes[0].properties, { tension: 0.5 });
});

test("audio/curve binary round-trip", () => {
  const f32 = new Float32Array([0.25, -1.5, 0]);
  assert.deepStrictEqual(Array.from(b64ToF32(f32ToB64(f32))), Array.from(f32));
  const f64 = new Float64Array([0.1, 1e300, NaN]);
  const back = b64ToF64(f64ToB64(f64));
  assert.strictEqual(back[0], 0.1);
  assert.strictEqual(back[1], 1e300);
  assert(Number.isNaN(back[2]));
});

test("tolerant decode accepts plain natives", () => {
  assert.strictEqual(decodeValue(1.5), 1.5);
  assert.strictEqual(decodeValue("x"), "x");
  assert.strictEqual(decodeValue(true), true);
});
