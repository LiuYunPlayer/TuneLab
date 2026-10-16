-- Unit tests for the Lua facade codec (run: lua tests/test_codec.lua)
package.path = "./?.lua;../?.lua;" .. package.path
local t = require("tunelab")
local json, ev, dv = t._json, t._encode_value, t._decode_value

local function eq(a, b, msg)
  if type(a) ~= type(b) then error((msg or "") .. ": type " .. type(a) .. " ~= " .. type(b)) end
  if type(a) == "table" then
    for k in pairs(a) do if a[k] ~= b[k] then error((msg or "") .. ": key " .. tostring(k)) end end
    for k in pairs(b) do if a[k] == nil then error((msg or "") .. ": extra key " .. tostring(k)) end end
  elseif a ~= b then
    if type(a) == "number" and a ~= a and b ~= b then return end   -- NaN == NaN for our purposes
    error((msg or "") .. ": " .. tostring(a) .. " ~= " .. tostring(b))
  end
end

-- tagged property round-trip
local tagged = ev(0.5); eq(tagged.n, 0.5, "number tag")
eq(ev(true), { b = true }, "bool tag")
eq(ev("x"), { s = "x" }, "string tag")
eq(dv({ n = "NaN" }), 0/0, "NaN decode")
eq(dv({ s = "hi" }), "hi", "string decode")
local nested = ev({ inner = 3 })
eq(nested.o.inner.n, 3, "nested object")
eq(dv(nested), { inner = 3 }, "nested round-trip")

-- JSON encode/decode
local s = json.encode({ a = 1, b = { 2, 3 }, c = "z" })
local back = json.decode(s)
eq(back.a, 1); eq(back.b[1], 2); eq(back.b[2], 3); eq(back.c, "z")
eq(json.decode(json.encode({ [1] = "x", [2] = "y" }))[1], "x", "array round-trip")
eq(json.decode(json.encode(0/0)), "NaN", "NaN stringifies as string on the wire")

-- walk encoder only touches "properties"
local proj = { tracks = { { parts = { { notes = { { pitch = 60, properties = { tension = 0.5 } } } } } } } }
local out = t._walk_encode(proj)
local note = out.tracks[1].parts[1].notes[1]
eq(note.pitch, 60, "pitch untouched")
eq(note.properties.tension.n, 0.5, "property tagged")
local rt = t._walk_decode(out)
eq(rt.tracks[1].parts[1].notes[1].properties.tension, 0.5, "walk decode")

-- base64 + f32 packing round-trip
local packed = t._b64_encode_f32({ 0.25, -1.5 })
local raw = t._unb64(packed)
local function f32_at(s, i)   -- i 为 1-based 样本序号
  if string.unpack then return (string.unpack("<f", s, (i - 1) * 4 + 1)) end
  local b1, b2, b3, b4 = s:byte((i-1)*4+1, (i-1)*4+4)
  local w = b1 + b2 * 256 + b3 * 65536 + b4 * 16777216
  local sign = math.floor(w / 2^31); local e = math.floor(w / 2^23) % 256
  local m = w % 2^23
  local v
  if e == 0 then v = m * 2^(-149) else v = (1 + m / 2^23) * 2^(e - 127) end
  if sign == 1 then v = -v end
  return v
end
eq(math.abs(f32_at(raw, 1) - 0.25) < 1e-6, true, "f32 sample 0")
eq(math.abs(f32_at(raw, 2) + 1.5) < 1e-6, true, "f32 sample 1")

print("lua facade codec tests: all passed")
