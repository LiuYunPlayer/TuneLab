-- LuaFormat sample — a .lpx file is one note per line: "<pitch> <pos> <dur> <lyric>".
-- CI target for PUC Lua 5.1-5.4 on ubuntu/macos (windows has no packaged lua yet).
-- The host test injects LUA_PATH pointing at scripting/lua; real users vendor the facade
-- into plugin/ (pip --target equivalent) and `require "tunelab"` finds it via the default path.
local tunelab = require("tunelab")

local function split(s)
  local out = {}
  for line in (s .. "\n"):gmatch("([^\n]*)\n") do
    if line ~= "" and line:sub(1, 1) ~= "#" then out[#out+1] = line end
  end
  return out
end

tunelab.format({
  import_project = function(path)
    local f = assert(io.open(path, "r"))
    local text = f:read("*a")
    f:close()
    local notes = {}
    for _, line in ipairs(split(text)) do
      local p, pos, dur, lyric = line:match("^(%S+)%s+(%S+)%s+(%S+)%s*(.*)$")
      if p then
        notes[#notes+1] = {
          pitch = tonumber(p), pos = tonumber(pos), dur = tonumber(dur),
          lyric = (lyric ~= "" and lyric) or "la",
          pronunciation = "",
          properties = { source = "lpx" },
          leadingPhonemes = tunelab.empty_array(), bodyPhonemes = tunelab.empty_array(), bodyOffset = 0.0,
        }
      end
    end
    return {
      tempos = { { pos = 0.0, bpm = 120.0 } },
      timeSignatures = { { barIndex = 0, numerator = 4, denominator = 4 } },
      tracks = { {
        name = "Lua Track", gain = 0.0, pan = 0.0, mute = false, solo = false, asRefer = true, color = "",
        parts = { {
          kind = "midi", name = "P", pos = 0.0, startOffset = 0.0, endOffset = 0.0, gain = 0.0,
          soundSource = { kind = "voice", type = "", id = "" },
          effects = tunelab.empty_array(), notes = notes,
          automations = {}, piecewiseAutomations = {},
          pitch = { segments = tunelab.empty_array() }, vibratos = tunelab.empty_array(), properties = {},
        } },
      } },
    }
  end,

  export_project = function(path, project)
    local lines = {}
    for _, track in ipairs((project or {}).tracks or {}) do
      for _, part in ipairs(track.parts or {}) do
        for _, note in ipairs(part.notes or {}) do
          lines[#lines+1] = string.format("%s %s %s %s",
            tostring(note.pitch), tostring(note.pos), tostring(note.dur), note.lyric or "la")
        end
      end
    end
    local f = assert(io.open(path, "w"))
    f:write(table.concat(lines, "\n") .. (#lines > 0 and "\n" or ""))
    f:close()
  end,
})

tunelab.serve()
