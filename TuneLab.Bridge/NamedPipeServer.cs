using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Bridge;

/// <summary>
/// Server for handling named pipe communication with VST3 plugin clients.
/// Supports multiple concurrent connections.
/// </summary>
public class NamedPipeServer : IDisposable
{
    private readonly ConcurrentDictionary<string, PipeConnection> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;
    
    /// <summary>
    /// Event raised when a message is received from a client.
    /// </summary>
    public event Action<string, BridgeMessage>? MessageReceived;
    
    /// <summary>
    /// Event raised when a client connects.
    /// </summary>
    public event Action<string>? ClientConnected;
    
    /// <summary>
    /// Event raised when a client disconnects.
    /// </summary>
    public event Action<string>? ClientDisconnected;
    
    /// <summary>
    /// Number of currently connected clients.
    /// </summary>
    public int ConnectionCount => _connections.Count;
    
    /// <summary>
    /// Starts the named pipe server.
    /// </summary>
    public void Start()
    {
        if (_cts != null) return;
        
        _cts = new CancellationTokenSource();
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
        
        Log.Info("NamedPipeServer: Started");
    }
    
    /// <summary>
    /// Stops the named pipe server and disconnects all clients.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();
        
        try
        {
            _acceptTask?.Wait(1000);
        }
        catch { }
        
        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;
        
        Log.Info("NamedPipeServer: Stopped");
    }
    
    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Use Byte transmission mode for compatibility with JUCE NamedPipe
                var pipeServer = new NamedPipeServerStream(
                    BridgeProtocol.PipeName,
                    PipeDirection.InOut,
                    BridgeProtocol.MaxClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                
                await pipeServer.WaitForConnectionAsync(ct);
                
                if (ct.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }
                
                // Generate temporary ID until client sends connect message
                var tempId = Guid.NewGuid().ToString("N")[..8];
                var connection = new PipeConnection(tempId, pipeServer);
                
                connection.MessageReceived += OnConnectionMessageReceived;
                connection.Disconnected += OnConnectionDisconnected;
                
                _connections[tempId] = connection;
                connection.StartReading(ct);
                
                Log.Info($"NamedPipeServer: Client connected (temp id: {tempId})");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error($"NamedPipeServer: Error accepting connection: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
    }
    
    private void OnConnectionMessageReceived(PipeConnection connection, BridgeMessage message)
    {
        // Handle connect message specially to update client ID
        if (message.Type == BridgeMessageType.Command && message.GetAction() == BridgeActions.Connect)
        {
            var clientId = message.GetPayloadValue<string>("clientId");
            if (!string.IsNullOrEmpty(clientId) && clientId != connection.ClientId)
            {
                // Update the connection's client ID
                _connections.TryRemove(connection.ClientId, out _);
                connection.ClientId = clientId;
                _connections[clientId] = connection;
                
                Log.Info($"NamedPipeServer: Client registered with ID: {clientId}");
                ClientConnected?.Invoke(clientId);
            }
        }
        
        MessageReceived?.Invoke(connection.ClientId, message);
    }
    
    private void OnConnectionDisconnected(PipeConnection connection)
    {
        if (_connections.TryRemove(connection.ClientId, out _))
        {
            Log.Info($"NamedPipeServer: Client disconnected: {connection.ClientId}");
            ClientDisconnected?.Invoke(connection.ClientId);
        }
        connection.Dispose();
    }
    
    /// <summary>
    /// Sends a message to a specific client.
    /// </summary>
    /// <param name="clientId">Target client ID</param>
    /// <param name="message">Message to send</param>
    /// <returns>True if sent successfully</returns>
    public bool SendMessage(string clientId, BridgeMessage message)
    {
        if (_connections.TryGetValue(clientId, out var connection))
        {
            return connection.SendMessage(message);
        }
        return false;
    }
    
    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    /// <param name="message">Message to broadcast</param>
    public void BroadcastMessage(BridgeMessage message)
    {
        foreach (var connection in _connections.Values)
        {
            connection.SendMessage(message);
        }
    }
    
    /// <summary>
    /// Checks if a client is connected.
    /// </summary>
    public bool IsClientConnected(string clientId)
    {
        return _connections.ContainsKey(clientId);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

/// <summary>
/// Represents a single pipe connection to a client.
/// </summary>
internal class PipeConnection : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly object _writeLock = new();
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readTask;
    private bool _disposed;
    
    public string ClientId { get; set; }
    
    public event Action<PipeConnection, BridgeMessage>? MessageReceived;
    public event Action<PipeConnection>? Disconnected;
    
    public PipeConnection(string clientId, NamedPipeServerStream pipe)
    {
        ClientId = clientId;
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }
    
    public void StartReading(CancellationToken ct)
    {
        _readTask = Task.Run(async () => await ReadLoopAsync(ct), ct);
    }
    
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Log.Debug($"PipeConnection[{ClientId}]: ReadLoop started, IsConnected={_pipe.IsConnected}");
        try
        {
            while (!ct.IsCancellationRequested && _pipe.IsConnected)
            {
                Log.Debug($"PipeConnection[{ClientId}]: Waiting for data... IsConnected={_pipe.IsConnected}");
                var line = await _reader!.ReadLineAsync();
                if (line == null)
                {
                    // Client disconnected
                    Log.Info($"PipeConnection[{ClientId}]: ReadLineAsync returned null (client disconnected)");
                    break;
                }
                
                Log.Debug($"PipeConnection[{ClientId}]: Received: {(line.Length > 100 ? line.Substring(0, 100) + "..." : line)}");
                var message = BridgeMessage.Deserialize(line);
                if (message != null)
                {
                    MessageReceived?.Invoke(this, message);
                }
            }
            Log.Info($"PipeConnection[{ClientId}]: ReadLoop exited normally. IsCancelled={ct.IsCancellationRequested}, IsConnected={_pipe.IsConnected}");
        }
        catch (IOException ex)
        {
            // Pipe broken - client disconnected
            Log.Info($"PipeConnection[{ClientId}]: IOException in ReadLoop: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
            Log.Info($"PipeConnection[{ClientId}]: ReadLoop cancelled");
        }
        catch (Exception ex)
        {
            Log.Error($"PipeConnection[{ClientId}]: Read error: {ex.Message}");
        }
        finally
        {
            Log.Info($"PipeConnection[{ClientId}]: ReadLoop finally block, invoking Disconnected");
            Disconnected?.Invoke(this);
        }
    }
    
    public bool SendMessage(BridgeMessage message)
    {
        if (_disposed || !_pipe.IsConnected) return false;
        
        try
        {
            lock (_writeLock)
            {
                _writer!.WriteLine(message.Serialize());
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"PipeConnection: Send error: {ex.Message}");
            return false;
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe.Dispose();
        }
        catch { }
        
        _reader = null;
        _writer = null;
    }
}
