using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.PluginHost;

/// <summary>
/// Event arguments for plugin scan progress
/// </summary>
public class ScanProgressEventArgs : EventArgs
{
    public string CurrentPath { get; }
    public int PluginsFound { get; }
    public int TotalScanned { get; }

    public ScanProgressEventArgs(string currentPath, int found, int total)
    {
        CurrentPath = currentPath;
        PluginsFound = found;
        TotalScanned = total;
    }
}

/// <summary>
/// Event arguments for plugin scan completion
/// </summary>
public class ScanCompleteEventArgs : EventArgs
{
    public int TotalPluginsFound { get; }

    public ScanCompleteEventArgs(int total)
    {
        TotalPluginsFound = total;
    }
}

/// <summary>
/// Main manager class for the plugin host system
/// </summary>
public sealed class PluginHostManager : IDisposable
{
    private static PluginHostManager? _instance;
    private static readonly object _lock = new();

    private bool _disposed;
    private readonly List<PluginInstance> _loadedInstances = new();

    // Keep delegates alive to prevent GC
    private PluginScanProgressCallback? _progressCallback;
    private PluginScanCompleteCallback? _completeCallback;

    /// <summary>
    /// Event raised during plugin scanning
    /// </summary>
    public event EventHandler<ScanProgressEventArgs>? ScanProgress;

    /// <summary>
    /// Event raised when plugin scanning is complete
    /// </summary>
    public event EventHandler<ScanCompleteEventArgs>? ScanComplete;

    /// <summary>
    /// Get the singleton instance of the plugin host manager
    /// </summary>
    public static PluginHostManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PluginHostManager();
                }
            }
            return _instance;
        }
    }

    private PluginHostManager()
    {
    }

    /// <summary>
    /// Initialize the plugin host system
    /// </summary>
    public void Initialize()
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_Initialize();
        if (result != PluginHostError.Ok && result != PluginHostError.AlreadyInitialized)
        {
            throw new PluginHostException($"Failed to initialize plugin host: {GetLastError()}", result);
        }
    }

    /// <summary>
    /// Shutdown the plugin host system
    /// </summary>
    public void Shutdown()
    {
        ThrowIfDisposed();

        // Dispose all loaded instances
        lock (_loadedInstances)
        {
            foreach (var instance in _loadedInstances.ToArray())
            {
                instance.Dispose();
            }
            _loadedInstances.Clear();
        }

        NativeMethods.PluginHost_Shutdown();
    }

    /// <summary>
    /// Check if the plugin host is initialized
    /// </summary>
    public bool IsInitialized => NativeMethods.PluginHost_IsInitialized();

    /// <summary>
    /// Get the last error message
    /// </summary>
    public string GetLastError()
    {
        var buffer = new StringBuilder(1024);
        NativeMethods.PluginHost_GetLastError(buffer, buffer.Capacity);
        return buffer.ToString();
    }

    // ========================================================================
    // Plugin Scanning
    // ========================================================================

    /// <summary>
    /// Add a directory to scan for plugins
    /// </summary>
    public void AddScanPath(string path)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_AddScanPath(path);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to add scan path: {GetLastError()}", result);
        }
    }

    /// <summary>
    /// Remove a directory from the scan paths
    /// </summary>
    public void RemoveScanPath(string path)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_RemoveScanPath(path);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to remove scan path: {GetLastError()}", result);
        }
    }

    /// <summary>
    /// Clear all scan paths
    /// </summary>
    public void ClearScanPaths()
    {
        ThrowIfDisposed();
        NativeMethods.PluginHost_ClearScanPaths();
    }

    /// <summary>
    /// Start scanning for plugins asynchronously
    /// </summary>
    public Task ScanPluginsAsync()
    {
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<bool>();

        _progressCallback = (path, found, total, userData) =>
        {
            ScanProgress?.Invoke(this, new ScanProgressEventArgs(path, found, total));
        };

        _completeCallback = (totalFound, userData) =>
        {
            ScanComplete?.Invoke(this, new ScanCompleteEventArgs(totalFound));
            tcs.TrySetResult(true);
        };

        var result = NativeMethods.PluginHost_StartScan(_progressCallback, _completeCallback, IntPtr.Zero);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to start scan: {GetLastError()}", result);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Stop the current plugin scan
    /// </summary>
    public void StopScan()
    {
        ThrowIfDisposed();
        NativeMethods.PluginHost_StopScan();
    }

    /// <summary>
    /// Check if a scan is currently in progress
    /// </summary>
    public bool IsScanning => NativeMethods.PluginHost_IsScanning();

    /// <summary>
    /// Get the number of discovered plugins
    /// </summary>
    public int PluginCount => NativeMethods.PluginHost_GetPluginCount();

    /// <summary>
    /// Get information about a discovered plugin by index
    /// </summary>
    public PluginInfo GetPluginInfo(int index)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetPluginInfo(index, out var info);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to get plugin info: {GetLastError()}", result);
        }

        return info;
    }

    /// <summary>
    /// Get information about a discovered plugin by UID
    /// </summary>
    public PluginInfo GetPluginInfo(string uid)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_GetPluginInfoByUid(uid, out var info);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to get plugin info: {GetLastError()}", result);
        }

        return info;
    }

    /// <summary>
    /// Get all discovered plugins
    /// </summary>
    public IEnumerable<PluginInfo> GetAllPlugins()
    {
        ThrowIfDisposed();

        int count = PluginCount;
        for (int i = 0; i < count; i++)
        {
            yield return GetPluginInfo(i);
        }
    }

    // ========================================================================
    // Plugin Loading
    // ========================================================================

    /// <summary>
    /// Load a plugin from a file path
    /// </summary>
    public PluginInstance LoadPlugin(string filePath)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_LoadPlugin(filePath, out var handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to load plugin: {GetLastError()}", result);
        }

        var instance = new PluginInstance(handle, this);
        
        lock (_loadedInstances)
        {
            _loadedInstances.Add(instance);
        }

        return instance;
    }

    /// <summary>
    /// Load a plugin by its unique ID (must have been scanned first)
    /// </summary>
    public PluginInstance LoadPluginByUid(string uid)
    {
        ThrowIfDisposed();

        var result = NativeMethods.PluginHost_LoadPluginByUid(uid, out var handle);
        if (result != PluginHostError.Ok)
        {
            throw new PluginHostException($"Failed to load plugin: {GetLastError()}", result);
        }

        var instance = new PluginInstance(handle, this);
        
        lock (_loadedInstances)
        {
            _loadedInstances.Add(instance);
        }

        return instance;
    }

    internal void UnregisterInstance(PluginInstance instance)
    {
        lock (_loadedInstances)
        {
            _loadedInstances.Remove(instance);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginHostManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Shutdown();
        _disposed = true;
        _instance = null;
    }
}

/// <summary>
/// Exception thrown by plugin host operations
/// </summary>
public class PluginHostException : Exception
{
    public PluginHostError ErrorCode { get; }

    public PluginHostException(string message, PluginHostError errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public PluginHostException(string message, PluginHostError errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
