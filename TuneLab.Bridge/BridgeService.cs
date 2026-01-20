using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Bridge;

/// <summary>
/// Main bridge service that manages communication between TuneLab and VST3 plugin clients.
/// This is a singleton service that should be started when TuneLab launches.
/// </summary>
public class BridgeService : IDisposable
{
    private static BridgeService? _instance;
    private static readonly object _instanceLock = new();
    
    private readonly NamedPipeServer _pipeServer;
    private readonly ConcurrentDictionary<string, BridgeClient> _clients = new();
    private CancellationTokenSource? _audioPumpCts;
    private Task? _audioPumpTask;
    private bool _disposed;
    
    // Callback for getting track list from TuneLab
    private Func<IReadOnlyList<TrackInfo>>? _getTrackListCallback;
    
    // Callback for getting audio data for a track
    private Func<string?, int, int, bool, float[]>? _getAudioDataCallback;
    
    // Callback for transport control
    private Action<bool, double>? _transportControlCallback;
    
    /// <summary>
    /// Gets the singleton instance of the bridge service.
    /// </summary>
    public static BridgeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new BridgeService();
                }
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Event raised when a client connects.
    /// </summary>
    public event Action<BridgeClient>? ClientConnected;
    
    /// <summary>
    /// Event raised when a client disconnects.
    /// </summary>
    public event Action<BridgeClient>? ClientDisconnected;
    
    /// <summary>
    /// Event raised when a client requests transport control.
    /// </summary>
    public event Action<string, bool, double>? TransportRequested;
    
    /// <summary>
    /// Number of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;
    
    /// <summary>
    /// List of connected client IDs.
    /// </summary>
    public IEnumerable<string> ConnectedClientIds => _clients.Keys;
    
    private BridgeService()
    {
        _pipeServer = new NamedPipeServer();
        _pipeServer.MessageReceived += OnPipeMessageReceived;
        _pipeServer.ClientConnected += OnPipeClientConnected;
        _pipeServer.ClientDisconnected += OnPipeClientDisconnected;
    }
    
    /// <summary>
    /// Starts the bridge service.
    /// </summary>
    public void Start()
    {
        if (_audioPumpTask != null) return;
        
        _pipeServer.Start();
        
        _audioPumpCts = new CancellationTokenSource();
        _audioPumpTask = Task.Run(() => AudioPumpLoop(_audioPumpCts.Token));
        
        Log.Info("BridgeService: Started");
    }
    
    /// <summary>
    /// Stops the bridge service.
    /// </summary>
    public void Stop()
    {
        _audioPumpCts?.Cancel();
        
        try
        {
            _audioPumpTask?.Wait(1000);
        }
        catch { }
        
        _audioPumpCts?.Dispose();
        _audioPumpCts = null;
        _audioPumpTask = null;
        
        _pipeServer.Stop();
        
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
        
        Log.Info("BridgeService: Stopped");
    }
    
    /// <summary>
    /// Sets the callback for getting the track list.
    /// </summary>
    public void SetTrackListCallback(Func<IReadOnlyList<TrackInfo>> callback)
    {
        _getTrackListCallback = callback;
    }
    
    /// <summary>
    /// Sets the callback for getting audio data.
    /// Callback parameters: (trackId, position, sampleCount, isStereo) -> samples
    /// </summary>
    public void SetAudioDataCallback(Func<string?, int, int, bool, float[]> callback)
    {
        _getAudioDataCallback = callback;
    }
    
    /// <summary>
    /// Sets the callback for transport control.
    /// Callback parameters: (isPlaying, position)
    /// </summary>
    public void SetTransportControlCallback(Action<bool, double> callback)
    {
        _transportControlCallback = callback;
    }
    
    /// <summary>
    /// Notifies all clients that the track list has changed.
    /// </summary>
    public void NotifyTrackListChanged()
    {
        var tracks = _getTrackListCallback?.Invoke() ?? new List<TrackInfo>();
        var message = BridgeMessage.CreateEvent(BridgeEvents.TrackListChanged, new TrackListPayload { Tracks = tracks.ToList() });
        _pipeServer.BroadcastMessage(message);
    }
    
    /// <summary>
    /// Notifies all clients of a transport state change.
    /// </summary>
    public void NotifyTransportChanged(bool isPlaying, double position)
    {
        var message = BridgeMessage.CreateEvent(BridgeEvents.TransportChanged, new TransportPayload
        {
            State = isPlaying ? "play" : "pause",
            Position = position
        });
        _pipeServer.BroadcastMessage(message);
    }
    
    /// <summary>
    /// Gets a specific client by ID.
    /// </summary>
    public BridgeClient? GetClient(string clientId)
    {
        return _clients.TryGetValue(clientId, out var client) ? client : null;
    }
    
    private void OnPipeClientConnected(string clientId)
    {
        // Client connected but not yet initialized - wait for Connect command
    }
    
    private void OnPipeClientDisconnected(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            ClientDisconnected?.Invoke(client);
            client.Dispose();
        }
    }
    
    private void OnPipeMessageReceived(string clientId, BridgeMessage message)
    {
        try
        {
            switch (message.Type)
            {
                case BridgeMessageType.Command:
                    HandleCommand(clientId, message);
                    break;
                default:
                    Log.Warning($"BridgeService: Unexpected message type from {clientId}: {message.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"BridgeService: Error handling message from {clientId}: {ex.Message}");
            SendErrorResponse(clientId, message.Id ?? "", ex.Message);
        }
    }
    
    private void HandleCommand(string clientId, BridgeMessage message)
    {
        var action = message.GetAction();
        
        switch (action)
        {
            case BridgeActions.Connect:
                HandleConnect(clientId, message);
                break;
                
            case BridgeActions.Disconnect:
                HandleDisconnect(clientId, message);
                break;
                
            case BridgeActions.GetTrackList:
                HandleGetTrackList(clientId, message);
                break;
                
            case BridgeActions.SelectTrack:
                HandleSelectTrack(clientId, message);
                break;
                
            case BridgeActions.Transport:
                HandleTransport(clientId, message);
                break;
                
            case BridgeActions.Seek:
                HandleSeek(clientId, message);
                break;
                
            default:
                Log.Warning($"BridgeService: Unknown action from {clientId}: {action}");
                SendErrorResponse(clientId, message.Id ?? "", $"Unknown action: {action}");
                break;
        }
    }
    
    private void HandleConnect(string clientId, BridgeMessage message)
    {
        var sampleRate = message.GetPayloadValue<int>("sampleRate");
        var bufferSize = message.GetPayloadValue<int>("bufferSize");
        
        if (sampleRate <= 0) sampleRate = 48000;
        if (bufferSize <= 0) bufferSize = 512;
        
        // Create or reinitialize client
        if (!_clients.TryGetValue(clientId, out var client))
        {
            client = new BridgeClient(clientId);
            _clients[clientId] = client;
        }
        
        if (client.Initialize(sampleRate, bufferSize))
        {
            var tracks = _getTrackListCallback?.Invoke() ?? new List<TrackInfo>();
            var response = BridgeMessage.CreateResponse(message.Id ?? "", true, new
            {
                tracks = tracks.ToList(),
                shmName = BridgeProtocol.ShmNamePrefix + clientId
            });
            _pipeServer.SendMessage(clientId, response);
            
            ClientConnected?.Invoke(client);
            Log.Info($"BridgeService: Client {clientId} connected successfully");
        }
        else
        {
            _clients.TryRemove(clientId, out _);
            SendErrorResponse(clientId, message.Id ?? "", "Failed to initialize shared memory");
        }
    }
    
    private void HandleDisconnect(string clientId, BridgeMessage message)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            ClientDisconnected?.Invoke(client);
            client.Dispose();
        }
        
        var response = BridgeMessage.CreateResponse(message.Id ?? "", true);
        _pipeServer.SendMessage(clientId, response);
    }
    
    private void HandleGetTrackList(string clientId, BridgeMessage message)
    {
        var tracks = _getTrackListCallback?.Invoke() ?? new List<TrackInfo>();
        var response = BridgeMessage.CreateResponse(message.Id ?? "", true, new TrackListPayload { Tracks = tracks.ToList() });
        _pipeServer.SendMessage(clientId, response);
    }
    
    private void HandleSelectTrack(string clientId, BridgeMessage message)
    {
        var trackId = message.GetPayloadValue<string>("trackId");
        
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.SelectTrack(trackId);
            var response = BridgeMessage.CreateResponse(message.Id ?? "", true);
            _pipeServer.SendMessage(clientId, response);
        }
        else
        {
            SendErrorResponse(clientId, message.Id ?? "", "Client not found");
        }
    }
    
    private void HandleTransport(string clientId, BridgeMessage message)
    {
        var state = message.GetPayloadValue<string>("state") ?? "pause";
        var position = message.GetPayloadValue<double>("position");
        var isPlaying = state == "play";
        
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.UpdateTransport(isPlaying, position);
        }
        
        // Forward to TuneLab
        _transportControlCallback?.Invoke(isPlaying, position);
        TransportRequested?.Invoke(clientId, isPlaying, position);
        
        var response = BridgeMessage.CreateResponse(message.Id ?? "", true);
        _pipeServer.SendMessage(clientId, response);
    }
    
    private void HandleSeek(string clientId, BridgeMessage message)
    {
        var position = message.GetPayloadValue<double>("position");
        
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.Seek(position);
        }
        
        // Forward to TuneLab
        _transportControlCallback?.Invoke(false, position);
        
        var response = BridgeMessage.CreateResponse(message.Id ?? "", true);
        _pipeServer.SendMessage(clientId, response);
    }
    
    private void SendErrorResponse(string clientId, string messageId, string error)
    {
        var response = BridgeMessage.CreateResponse(messageId, false, new { error });
        _pipeServer.SendMessage(clientId, response);
    }
    
    /// <summary>
    /// Audio pump loop that continuously feeds audio data to connected clients.
    /// </summary>
    private async Task AudioPumpLoop(CancellationToken ct)
    {
        const int pumpIntervalMs = 20; // 20ms = 50 pumps per second
        const int samplesPerPump = 960; // ~20ms at 48kHz
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var client in _clients.Values)
                {
                    if (!client.IsConnected) continue;
                    
                    // Only pump audio if playing
                    if (!client.TransportState.IsPlaying) continue;
                    
                    // Check if buffer has space
                    var availableSpace = client.GetAvailableSpace();
                    if (availableSpace < samplesPerPump) continue;
                    
                    // Get audio data from TuneLab
                    if (_getAudioDataCallback != null)
                    {
                        var position = (int)(client.TransportState.Position * client.SampleRate);
                        var samples = _getAudioDataCallback(client.SelectedTrackId, position, samplesPerPump, true);
                        
                        if (samples != null && samples.Length >= samplesPerPump * 2)
                        {
                            client.WriteInterleavedAudio(samples, samplesPerPump);
                            
                            // Update transport position
                            client.TransportState.Position += (double)samplesPerPump / client.SampleRate;
                        }
                    }
                }
                
                await Task.Delay(pumpIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error($"BridgeService: Audio pump error: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        Stop();
        _pipeServer.Dispose();
        
        lock (_instanceLock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        Log.Info("BridgeService: Disposed");
    }
}
