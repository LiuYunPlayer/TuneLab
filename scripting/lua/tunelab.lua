-- TuneLab scripted-plugin facade (pure Lua, zero dependency).
--
-- Speaks the sidecar wire protocol (spec: scripting/wire/v1.json): LSP-style
-- Content-Length frames of JSON-RPC 2.0 over stdio. Works on PUC Lua 5.1-5.4 and
-- LuaJIT. The inline plane is the only audio plane here (no mmap in stock Lua);
-- long user handlers block `ping` replies, so keep them chunked or move to the
-- shm fast lane when it lands.
--
-- Usage (package entry, e.g. plugin/main.lua):
--
--   local tunelab = require("tunelab")
--   tunelab.format({
--     import_project = function(path) ... return project end,
--     export_project = function(path, project) ... end,
--   })
--   tunelab.serve()

local tunelab = { VERSION = "0.1.0", WIRE_PROTOCOL = 1 }

-- ---------------------------------------------------------------- JSON (minimal)
local json = {}

local function is_array(t)
  local n = 0
  for k in pairs(t) do
    if type(k) ~= "number" then return false end
    n = n + 1
  end
  if n == 0 then return false end          -- {} encodes as object unless empty_array()
  for i = 1, n do if t[i] == nil then return false end end
  return true
end

local EmptyArray = {}
function tunelab.empty_array() return setmetatable({}, EmptyArray) end

local function encode(v, out)
  local tv = type(v)
  if v == nil then out[#out+1] = "null"
  elseif tv == "boolean" then out[#out+1] = v and "true" or "false"
  elseif tv == "number" then
    if v ~= v then out[#out+1] = '"NaN"'
    elseif v == math.huge then out[#out+1] = '"Infinity"'
    elseif v == -math.huge then out[#out+1] = '"-Infinity"'
    else out[#out+1] = string.format("%.17g", v) end
  elseif tv == "string" then
    out[#out+1] = string.format("%q", v):gsub("\\\n", "\\n")
  elseif tv == "table" then
    if getmetatable(v) == EmptyArray then out[#out+1] = "[]" return end
    if is_array(v) then
      out[#out+1] = "["
      for i = 1, #v do if i > 1 then out[#out+1] = "," end encode(v[i], out) end
      out[#out+1] = "]"
    else
      out[#out+1] = "{"
      local first = true
      local keys = {}
      for k in pairs(v) do keys[#keys+1] = tostring(k) end
      table.sort(keys)
      for _, k in ipairs(keys) do
        if not first then out[#out+1] = "," end
        first = false
        out[#out+1] = string.format("%q", k) .. ":"
        encode(v[k], out)
      end
      out[#out+1] = "}"
    end
  else error("json: cannot encode " .. tv) end
end

function json.encode(v) local out = {}; encode(v, out); return table.concat(out) end

local function decode(s, pos)
  while s:sub(pos, pos):match("%s") do pos = pos + 1 end
  local c = s:sub(pos, pos)
  if c == "{" then
    local t, first = {}, true
    pos = pos + 1
    while s:sub(pos, pos) ~= "}" do
      first = false
      local k, v
      k, pos = decode(s, pos)
      pos = s:find(":", pos, true)
      v, pos = decode(s, pos + 1)
      t[k] = v
      while s:sub(pos, pos):match("[%s,]") do pos = pos + 1 end   -- 吃掉分隔符，循环直接落在下一键
    end
    return t, pos + 1
  elseif c == "[" then
    local t, first = {}, true
    pos = pos + 1
    while s:sub(pos, pos) ~= "]" do
      first = false
      local v
      v, pos = decode(s, pos)
      t[#t+1] = v
      while s:sub(pos, pos):match("[%s,]") do pos = pos + 1 end
    end
    return t, pos + 1
  elseif c == '"' then
    local buf, i = {}, pos + 1
    while true do
      local ch = s:sub(i, i)
      if ch == '"' then break end
      if ch == "\\" then
        local esc = s:sub(i + 1, i + 1)
        if esc == "n" then buf[#buf+1] = "\n"
        elseif esc == "t" then buf[#buf+1] = "\t"
        elseif esc == "r" then buf[#buf+1] = "\r"
        elseif esc == "u" then
          local cp = tonumber(s:sub(i + 2, i + 5), 16)
          if cp < 0x80 then buf[#buf+1] = string.char(cp)
          elseif cp < 0x800 then buf[#buf+1] = string.char(0xC0 + math.floor(cp/0x40), 0x80 + cp % 0x40)
          else buf[#buf+1] = string.char(0xE0 + math.floor(cp/0x1000), 0x80 + math.floor(cp/0x40) % 0x40, 0x80 + cp % 0x40) end
          i = i + 4
        else buf[#buf+1] = esc end
        i = i + 2
      else buf[#buf+1] = ch; i = i + 1 end
    end
    return table.concat(buf), i + 1
  elseif s:sub(pos, pos + 3) == "true" then return true, pos + 4
  elseif s:sub(pos, pos + 4) == "false" then return false, pos + 5
  elseif s:sub(pos, pos + 3) == "null" then return nil, pos + 4
  else
    -- 注意：'^' 模式无视 pos 参数（永远锚串首），这里必须用非锚定 + 起始位搜索
    local numstr = s:match("[-+0-9.eE]+", pos)
    assert(numstr, "json: unexpected token at " .. pos)
    return tonumber(numstr), pos + #numstr
  end
end

function json.decode(s) local v, pos = decode(s, 1); return v end

-- ---------------------------------------------------------------- property wire
local function encode_value(v)
  if v == nil then return nil end
  local t = type(v)
  if t == "boolean" then return { b = v } end
  if t == "number" then
    if v ~= v then return { n = "NaN" }
    elseif v == math.huge then return { n = "Infinity" }
    elseif v == -math.huge then return { n = "-Infinity" } end
    return { n = v }
  end
  if t == "string" then return { s = v } end
  if t == "table" then
    if #v > 0 then local a = {}; for i = 1, #v do a[i] = encode_value(v[i]) end; return { a = a } end
    local o = {}
    for k, val in pairs(v) do o[tostring(k)] = encode_value(val) end
    return { o = o }
  end
  error("cannot encode property value of type " .. t)
end

local function decode_value(v)
  if v == nil then return nil end
  if type(v) ~= "table" then return v end
  local tag, payload = next(v)
  if next(v, tag) == nil then
    if tag == "b" or tag == "s" then return payload end
    if tag == "n" then
      if type(payload) == "string" then
        if payload == "NaN" then return 0/0 end
        if payload == "Infinity" then return math.huge end
        if payload == "-Infinity" then return -math.huge end
        return tonumber(payload)
      end
      return payload
    end
    if tag == "o" then
      local out = {}
      for k, x in pairs(payload or {}) do out[k] = decode_value(x) end
      return out
    end
    if tag == "a" then
      local out = {}
      for i, x in ipairs(payload or {}) do out[i] = decode_value(x) end
      return out
    end
  end
  local out = {}
  for k, x in pairs(v) do out[k] = decode_value(x) end
  return out
end

local function walk_encode(node)
  if type(node) ~= "table" then return node end
  local out = {}
  for k, v in pairs(node) do
    if k == "properties" and type(v) == "table" then
      local m = {}
      for pk, pv in pairs(v) do m[tostring(pk)] = encode_value(pv) end
      out[k] = m
    else
      out[k] = walk_encode(v)
    end
  end
  return out
end

local function walk_decode(node)
  if type(node) ~= "table" then return node end
  local out = {}
  for k, v in pairs(node) do
    if k == "properties" and type(v) == "table" then
      local m = {}
      for pk, pv in pairs(v) do m[pk] = decode_value(pv) end
      out[k] = m
    else
      out[k] = walk_decode(v)
    end
  end
  return out
end

-- ---------------------------------------------------------------- framing
local stdin, stdout = io.stdin, io.stdout
stdin:setvbuf("no")

local function read_message()
  local len
  while true do
    local line = stdin:read("l")
    if line == nil then return nil end
    if line == "" then break end
    local n = line:match("^[Cc]ontent-[Ll]ength:%s*(%d+)")
    if n then len = tonumber(n) end
  end
  if not len then return nil end
  local body = stdin:read(len)
  if not body then return nil end
  return json.decode(body)
end

local function write_message(obj)
  local body = json.encode(obj)
  stdout:write("Content-Length: " .. #body .. "\r\n\r\n" .. body)
  stdout:flush()
end

-- ---------------------------------------------------------------- registries
local format_handlers = {}          -- entry id -> {import_project, export_project, get_settings_config, apply_settings}
local effect_engines, voice_engines = {}, {}
local sessions = {}                 -- key -> {kind, engine, user, segments}
local pending = {}                  -- reverse-call id -> box
local seq = 0

local function reply(id, result) write_message({ jsonrpc = "2.0", id = id, result = result or {} }) end
local function reply_err(id, code, message) write_message({ jsonrpc = "2.0", id = id, error = { code = code, message = message } }) end
local function notify(method, params) write_message({ jsonrpc = "2.0", method = method, params = params or {} }) end

local function call_host(method, params)
  seq = seq + 1
  local id = "l" .. seq
  local box = {}
  pending[id] = box
  write_message({ jsonrpc = "2.0", id = id, method = method, params = params })
  while true do
    local msg = read_message()
    if not msg then os.exit(0) end
    if msg.method == nil and msg.id and tostring(msg.id) == id then
      if msg.error then error("host error on " .. method .. ": " .. tostring(msg.error.message)) end
      return msg.result or {}
    elseif msg.method and msg.id then
      dispatch(msg)                       -- nested inbound request while waiting (host reverse-call during our reverse-call)
    end
  end
end

-- ---------------------------------------------------------------- segments
local function segment_create(engine, key, offset, count, rate)
  local r = call_host("host.segment_create", { engine = engine, session = key,
      sample_offset = offset, sample_count = count, sample_rate = rate })
  return r.segment
end
local function segment_write(engine, key, seg, offset, samples)
  local b = {}
  for i = 1, #samples do b[i] = samples[i] end
  call_host("host.segment_write", { engine = engine, session = key, segment = seg,
      offset = offset, data = tunelab._b64_encode_f32(b) })
end
local function segment_commit(engine, key, seg)
  call_host("host.segment_commit", { engine = engine, session = key, segment = seg })
end

-- base64 (binary-safe, no external deps)
local B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
local function b64(s)
  local out, pad = {}, 0
  for i = 1, #s, 3 do
    local a, b, c = s:byte(i, i + 2)
    b = b or 0; c = c or 0; pad = (s:byte(i + 2)) and 0 or ((s:byte(i + 1)) and 1 or 2)
    local n = a * 65536 + b * 256 + c
    out[#out+1] = B64:sub(math.floor(n/262144) % 64 + 1, math.floor(n/262144) % 64 + 1)
    out[#out+1] = B64:sub(math.floor(n/4096) % 64 + 1, math.floor(n/4096) % 64 + 1)
    out[#out+1] = B64:sub(math.floor(n/64) % 64 + 1, math.floor(n/64) % 64 + 1)
    out[#out+1] = B64:sub(n % 64 + 1, n % 64 + 1)
  end
  local s = table.concat(out)
  if pad == 1 then s = s:sub(1, -2) .. "=" elseif pad == 2 then s = s:sub(1, -3) .. "==" end
  return s
end
local function unb64(s)
  s = s:gsub("[^" .. B64 .. "=]", ""):gsub("=", "")
  local out, acc, bits = {}, 0, 0
  for i = 1, #s do
    acc = acc * 64 + (B64:find(s:sub(i, i), 1, true) - 1)
    bits = bits + 6
    if bits >= 8 then bits = bits - 8; out[#out+1] = string.char(math.floor(acc / 2^bits) % 256) end
  end
  return table.concat(out)
end

-- little-endian f32/f64 (string.pack where available; manual fallback for 5.1/LuaJIT)
local pack_f32
if string.pack then
  pack_f32 = function(samples) local b = {} for i = 1, #samples do b[i] = string.pack("<f", samples[i]) end return table.concat(b) end
else
  pack_f32 = function(samples)
    local out = {}
    for i = 1, #samples do
      local v = samples[i]
      local sign = v < 0 and 1 or 0; v = math.abs(v)
      local m, e
      if v == 0 then m, e = 0, 0
      else
        e = math.floor(math.log(v, 2)); m = v / 2^e
        if e < -126 or e > 127 then m, e = 0, 255
        else m = math.floor((m - 1) * 2^23 + 0.5); e = e + 127 end
      end
      local w = sign * 2^31 + e * 2^23 + m
      out[i] = string.char(w % 256, math.floor(w / 256) % 256, math.floor(w / 65536) % 256, math.floor(w / 16777216) % 256)
    end
    return table.concat(out)
  end
end
function tunelab._b64_encode_f32(samples) return b64(pack_f32(samples)) end

local unpack_f64
if string.unpack then
  unpack_f64 = function(s) local out, i = {}, 1 for _ = 1, #s // 8 do out[#out+1], i = string.unpack("<d", s, i) end return out end
else
  unpack_f64 = function(s) error("f64 payloads need Lua 5.2+ (string.unpack)") end
end

-- ---------------------------------------------------------------- ctx API (effect/voice)
local function make_ctx(kind, engine, key, voice_id)
  local ctx = { kind = kind, engine = engine, session = key, voice_id = voice_id, segments = {}, cancelled = false }
  function ctx.input_read(offset, count)
    local r = call_host("host.input_read", { engine = engine, session = key, offset = offset or 0, count = count })
    return r
  end
  function ctx.properties(keys)
    local r = call_host("host.properties", { engine = engine, session = key, keys = keys })
    return decode_value({ o = r.values or {} })
  end
  function ctx.automation_tracks()
    return (call_host("host.automation_tracks", { engine = engine, session = key })).tracks or {}
  end
  function ctx.evaluate(track, positions)
    local packed = {}
    for i = 1, #positions do
      assert(string.pack, "ctx.evaluate needs Lua 5.2+ (string.pack)")
      packed[#packed + 1] = string.pack("<d", positions[i])
    end
    local r = call_host("host.evaluate", { engine = engine, session = key, track = track,
        positions = b64(table.concat(packed)) })
    return unpack_f64(unb64(r.values))
  end
  function ctx.output(offset, count, rate)
    local seg = segment_create(engine, key, offset, count, rate)
    ctx.segments[#ctx.segments+1] = seg
    return seg
  end
  function ctx.write(seg, offset, samples) segment_write(engine, key, seg, offset, samples) end
  function ctx.commit(seg) segment_commit(engine, key, seg) end
  function ctx.status(segments)
    notify(kind == "voice" and "voice.status" or "effect.status",
      { engine = engine, session = key, segments = segments })
  end
  function ctx.parameters(map)
    notify(kind == "voice" and "voice.parameters" or "effect.parameters", { engine = engine, session = key, parameters = map })
  end
  if kind == "voice" then
    function ctx.notes() return (call_host("host.notes", { engine = engine, session = key })).notes or {} end
    function ctx.snapshot(note_ids)
      local ids = {}
      for i, n in ipairs(note_ids) do ids[i] = type(n) == "table" and n.id or n end
      return (call_host("host.snapshot", { engine = engine, session = key, note_ids = ids })).snapshot
    end
    function ctx.pitch(segments) notify("voice.pitch", { engine = engine, session = key, segments = segments }) end
    function ctx.phonemes(syllables) notify("voice.phonemes", { engine = engine, session = key, syllables = syllables }) end
  end
  return ctx
end

-- ---------------------------------------------------------------- inbound dispatch
local function first_handler()
  local _, h = next(format_handlers)
  return h
end

function dispatch(msg)
  local method, params, id = msg.method, msg.params or {}, msg.id
  if method == "initialize" then
    local provides = {}
    for _, e in ipairs(params.entries or {}) do
      if e.type == "format" then
        local h = format_handlers[e.entry]
        if not h then
          h = first_handler()
          if h then format_handlers[e.entry] = h end          -- single-handler package binds to declared entry
        end
        if h then
          provides[#provides+1] = { type = "format", entry = e.entry,
            can_import = h.import_project ~= nil, can_export = h.export_project ~= nil,
            settings = h.get_settings_config ~= nil }
          if e.settings and h.apply_settings then h.apply_settings(decode_value({ o = e.settings })) end
        end
      else
        local eng = effect_engines[e.engine] or voice_engines[e.engine]
        if eng then
          provides[#provides+1] = { type = e.type, engine = e.engine, settings = eng.get_settings_config ~= nil }
          if e.settings and eng.apply_settings then eng.apply_settings(decode_value({ o = e.settings })) end
        end
      end
    end
    reply(id, {
      provides = provides,
      capabilities = { audio_plane = "inline", curve_plane = "inline" },
      runtime = { exe = arg and arg[0] or "lua", version = (_VERSION), arch = "unknown", flavor = "lua" },
      facade_version = tunelab.VERSION,
    })
  elseif method == "ping" then
    reply(id, {})
  elseif method == "shutdown" then
    if id then reply(id, {}) end
    os.exit(0)
  elseif method == "format.import" then
    local h = format_handlers[params.entry] or first_handler()
    local project = h.import_project(params.stream_ref)
    reply(id, { project = walk_encode(project or {}) })
  elseif method == "format.export" then
    local h = format_handlers[params.entry] or first_handler()
    h.export_project(params.stream_ref, walk_decode(params.project or {}))
    reply(id, {})
  elseif method == "engine.init" then
    local eng = effect_engines[params.engine] or voice_engines[params.engine]
    if eng and eng.init then eng.init() end
    reply(id, {})
  elseif method == "engine.destroy" then
    local eng = effect_engines[params.engine] or voice_engines[params.engine]
    if eng and eng.destroy then pcall(eng.destroy) end
    if id then reply(id, {}) end
  elseif method == "engine.create_session" then
    local key = params.session
    local kind = key:find("^voice:") and "voice" or "effect"
    local reg = kind == "voice" and voice_engines or effect_engines
    local eng = reg[params.engine]
    local ctx = make_ctx(kind, params.engine, key, params.voice_id)
    local user = eng.create_session and eng.create_session(ctx) or eng
    sessions[key] = { kind = kind, engine = params.engine, user = user, ctx = ctx }
    reply(id, {})
  elseif method == "session.dispose" then
    local s = sessions[params.session]
    sessions[params.session] = nil
    if s then
      for _, seg in ipairs(s.ctx.segments) do pcall(function() call_host("host.segment_dispose", { engine = s.engine, session = params.session, segment = seg }) end) end
      if s.user.dispose then pcall(s.user.dispose) end
    end
    if id then reply(id, {}) end
  elseif method == "effect.cancel" or method == "voice.cancel" then
    local s = sessions[params.session]
    if s then s.ctx.cancelled = true; if s.user.cancel then pcall(s.user.cancel, s.user) end end
  elseif method == "voice.dirty" then
    local s = sessions[params.session]
    if s and s.user.on_dirty then pcall(s.user.on_dirty, s.user, params.kind, params) end
    if id then reply(id, {}) end
  elseif method == "effect.process" then
    local s = assert(sessions[params.session], "unknown session")
    if s.user.process then s.user.process(s.ctx) end
    reply(id, {})
  elseif method == "effect.get_property_config" then
    local eng = effect_engines[params.engine]
    reply(id, { config = eng.get_property_config and eng.get_property_config(params.effects or {}) or { type = "object", properties = tunelab.empty_array() } })
  elseif method == "effect.get_automation_configs" then
    local eng = effect_engines[params.engine]
    reply(id, { tracks = eng.get_automation_configs and eng.get_automation_configs(params.effects or {}) or tunelab.empty_array() })
  elseif method == "effect.get_synthesized_parameter_configs" then
    local eng = effect_engines[params.engine]
    reply(id, { tracks = eng.get_synthesized_parameter_configs and eng.get_synthesized_parameter_configs(params.effects or {}) or tunelab.empty_array() })
  elseif method == "voice.get_catalog" then
    local sources, layout = voice_engines[params.engine].catalog()
    reply(id, { sources = sources, layout = layout or tunelab.empty_array() })
  elseif method == "voice.get_automation_configs" then
    reply(id, { tracks = voice_engines[params.engine].automation_configs(params.parts or {}) })
  elseif method == "voice.get_synthesized_parameter_configs" then
    local eng = voice_engines[params.engine]
    reply(id, { tracks = eng.synthesized_parameter_configs and eng.synthesized_parameter_configs(params.parts or {}) or tunelab.empty_array() })
  elseif method == "voice.get_part_property_config" then
    reply(id, { config = voice_engines[params.engine].part_property_config(params.parts or {}) })
  elseif method == "voice.get_note_property_config" then
    reply(id, { config = voice_engines[params.engine].note_property_config(params.part or {}, params.notes or {}) })
  elseif method == "voice.get_phoneme_property_configs" then
    local eng = voice_engines[params.engine]
    reply(id, { slots = eng.phoneme_property_configs and eng.phoneme_property_configs(params.part or {}, params.notes or {}) or tunelab.empty_array() })
  elseif method == "voice.default_lyric" then
    reply(id, { lyric = sessions[params.session].user.default_lyric or "la" })
  elseif method == "voice.is_continuation" then
    reply(id, { continuation = sessions[params.session].user.is_continuation(params.note) and true or false })
  elseif method == "voice.peek" then
    local r = sessions[params.session].user.peek(params.start, params["end"])
    reply(id, { range = r and { r[1], r[2] } })   -- absent key == null range on the host reader
  elseif method == "voice.synthesize" then
    local s = assert(sessions[params.session], "unknown session")
    s.user.synthesize(params.start, params["end"])
    reply(id, {})
  elseif method == "settings.get_config" then
    local h = format_handlers[params.entry]
    if h and h.get_settings_config then reply(id, { config = h.get_settings_config() })
    else reply_err(id, "bad_request", "no settings for entry " .. tostring(params.entry)) end
  elseif method == "settings.apply" then
    local h = format_handlers[params.entry]
    if h and h.apply_settings then h.apply_settings(decode_value({ o = params.values or {} })) end
    if id then reply(id, {}) end
  elseif id then
    reply_err(id, "bad_request", "unknown method " .. tostring(method))
  end
end

-- ---------------------------------------------------------------- registration + serve
function tunelab.format(handlers)
  format_handlers[handlers.entry or ("auto-" .. (next(format_handlers) and 1 or 0))] = handlers
  return handlers
end
function tunelab.effect(engine_id, obj) effect_engines[engine_id] = obj; return obj end
function tunelab.voice(engine_id, obj) voice_engines[engine_id] = obj; return obj end

function tunelab.serve()
  while true do
    local msg = read_message()
    if not msg then os.exit(0) end          -- stdin EOF = host died (defense line 1)
    if msg.method then
      local ok, err = pcall(dispatch, msg)
      if not ok and msg.id then reply_err(msg.id, "runtime_error", tostring(err)) end
    end
  end
end

function tunelab.log(level, message) notify("log.write", { level = level, message = tostring(message) }) end
tunelab.info = function(m) tunelab.log("info", m) end
tunelab.warn = function(m) tunelab.log("warning", m) end
tunelab.error = function(m) tunelab.log("error", m) end

-- expose codec for tests
tunelab._json = json
tunelab._encode_value = encode_value
tunelab._decode_value = decode_value
tunelab._walk_encode = walk_encode
tunelab._walk_decode = walk_decode
tunelab._b64 = b64
tunelab._unb64 = unb64

return tunelab
