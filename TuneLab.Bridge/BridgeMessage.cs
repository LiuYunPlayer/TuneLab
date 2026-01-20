using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace TuneLab.Bridge;

/// <summary>
/// Message type for bridge protocol.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum BridgeMessageType
{
    Command,
    Response,
    Event
}

/// <summary>
/// Base message structure for bridge communication.
/// </summary>
public class BridgeMessage
{
    [JsonProperty("type")]
    public BridgeMessageType Type { get; set; }
    
    [JsonProperty("id")]
    public string? Id { get; set; }
    
    [JsonProperty("payload")]
    public JObject? Payload { get; set; }
    
    public static BridgeMessage CreateCommand(string action, object? data = null)
    {
        var payload = new JObject { ["action"] = action };
        if (data != null)
        {
            foreach (var prop in JObject.FromObject(data).Properties())
            {
                payload[prop.Name] = prop.Value;
            }
        }
        
        return new BridgeMessage
        {
            Type = BridgeMessageType.Command,
            Id = Guid.NewGuid().ToString("N")[..8],
            Payload = payload
        };
    }
    
    public static BridgeMessage CreateResponse(string id, bool success, object? data = null)
    {
        var payload = new JObject { ["success"] = success };
        if (data != null)
        {
            payload["data"] = JToken.FromObject(data);
        }
        
        return new BridgeMessage
        {
            Type = BridgeMessageType.Response,
            Id = id,
            Payload = payload
        };
    }
    
    public static BridgeMessage CreateEvent(string eventName, object? data = null)
    {
        var payload = new JObject { ["event"] = eventName };
        if (data != null)
        {
            foreach (var prop in JObject.FromObject(data).Properties())
            {
                payload[prop.Name] = prop.Value;
            }
        }
        
        return new BridgeMessage
        {
            Type = BridgeMessageType.Event,
            Payload = payload
        };
    }
    
    public string GetAction() => Payload?["action"]?.Value<string>() ?? string.Empty;
    public string GetEvent() => Payload?["event"]?.Value<string>() ?? string.Empty;
    public bool GetSuccess() => Payload?["success"]?.Value<bool>() ?? false;
    
    public T? GetPayloadValue<T>(string key)
    {
        var token = Payload?[key];
        if (token == null) return default;
        return token.ToObject<T>();
    }
    
    public string Serialize() => JsonConvert.SerializeObject(this);
    
    public static BridgeMessage? Deserialize(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<BridgeMessage>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Command actions for the bridge protocol.
/// </summary>
public static class BridgeActions
{
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string GetTrackList = "getTrackList";
    public const string SelectTrack = "selectTrack";
    public const string Transport = "transport";
    public const string Seek = "seek";
    public const string RequestAudio = "requestAudio";
}

/// <summary>
/// Event names for the bridge protocol.
/// </summary>
public static class BridgeEvents
{
    public const string TrackListChanged = "trackListChanged";
    public const string TransportChanged = "transportChanged";
    public const string TrackChanged = "trackChanged";
    public const string ConnectionLost = "connectionLost";
}

/// <summary>
/// Connect command payload.
/// </summary>
public class ConnectPayload
{
    [JsonProperty("clientId")]
    public string ClientId { get; set; } = string.Empty;
    
    [JsonProperty("sampleRate")]
    public int SampleRate { get; set; } = 48000;
    
    [JsonProperty("bufferSize")]
    public int BufferSize { get; set; } = 512;
}

/// <summary>
/// Select track command payload.
/// </summary>
public class SelectTrackPayload
{
    [JsonProperty("trackId")]
    public string TrackId { get; set; } = string.Empty;
}

/// <summary>
/// Transport command payload.
/// </summary>
public class TransportPayload
{
    [JsonProperty("state")]
    public string State { get; set; } = "stop"; // "play", "pause", "stop"
    
    [JsonProperty("position")]
    public double Position { get; set; }
}

/// <summary>
/// Seek command payload.
/// </summary>
public class SeekPayload
{
    [JsonProperty("position")]
    public double Position { get; set; }
}

/// <summary>
/// Track list event payload.
/// </summary>
public class TrackListPayload
{
    [JsonProperty("tracks")]
    public List<TrackInfo> Tracks { get; set; } = new();
}
