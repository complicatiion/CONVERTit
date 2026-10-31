using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App;

/// <summary>Files and an optional action handed over by Explorer ("--action id file …") or a second start.</summary>
public sealed record ExternalRequest(string? Action, string[] Files);

internal static class Program
{
    private const string MutexName = @"Local\CONVERTit.App";
    private const string ShowSignalName = @"Local\CONVERTit.App.Show";

    /// <summary>Per user and session (Explorer starts one process per selected file; they are collected here).</summary>
    private static readonly string PipeName = $"CONVERTit.{Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>Signal to the running instance: show window (fallback when the pipe is not reachable).</summary>
    internal static EventWaitHandle? ShowSignal { get; private set; }

    /// <summary>Requests from this start and from later starts; the app drains it once the window exists.</summary>
    internal static ConcurrentQueue<ExternalRequest> Requests { get; } = new();

    /// <summary>Raised on a background thread when a request arrived.</summary>
    internal static event Action? RequestArrived;

    [STAThread]
    public static int Main(string[] args)
    {
        // convertit://open-folder?path=… from the "Open folder" button of a notification: open Explorer and exit.
        if (args.Length == 1 && OperatingSystem.IsWindows() && Notifications.HandleActivation(args[0]))
        {
            return 0;
        }

        // Uninstaller: remove the per-user Explorer menu and notification registration, then exit.
        if (args.Length == 1 && args[0] == "--unregister")
        {
            if (OperatingSystem.IsWindows())
            {
                ExplorerMenu.Unregister(Core.Media.MediaTypes.VideoExtensions
                    .Concat(Core.Media.MediaTypes.AudioExtensions).Concat(Core.Media.MediaTypes.ImageExtensions));
                Notifications.Unregister();
            }

            return 0;
        }

        var request = Parse(args);

        Mutex? mutex = null;
        if (OperatingSystem.IsWindows())
        {
            Shell.SetAppId(Notifications.AppId);

            // One instance per session; later starts hand their files over and exit.
            mutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                if (!Forward(request))
                {
                    try
                    {
                        using var existing = EventWaitHandle.OpenExisting(ShowSignalName);
                        existing.Set();
                    }
                    catch
                    {
                        // First instance not ready yet.
                    }
                }

                mutex.Dispose();
                return 0;
            }

            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            StartPipeServer();
        }

        if (request.Files.Length > 0 || request.Action is not null)
        {
            Requests.Enqueue(request);
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ShowSignal?.Dispose();
            mutex?.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    /// <summary>"--action video-mp4 a.mov b.mov" or plain paths ("Open with", drag onto the exe).</summary>
    internal static ExternalRequest Parse(IReadOnlyList<string> args)
    {
        string? action = null;
        var files = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--action" && i + 1 < args.Count)
            {
                action = args[++i].Trim().ToLowerInvariant();
                continue;
            }

            if (File.Exists(args[i]) || Directory.Exists(args[i]))
            {
                files.Add(Path.GetFullPath(args[i]));
            }
        }

        return new ExternalRequest(action, [.. files]);
    }

    private static bool Forward(ExternalRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // The first instance may still be starting (many files selected in Explorer start at the same time).
            client.Connect(10_000);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartPipeServer()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    var request = JsonSerializer.Deserialize<ExternalRequest>(text);
                    if (request is not null)
                    {
                        // Only existing local paths are accepted.
                        Requests.Enqueue(request with { Files = request.Files.Where(f => File.Exists(f) || Directory.Exists(f)).ToArray() });
                        RequestArrived?.Invoke();
                    }
                }
                catch
                {
                    // Broken message: wait for the next one.
                    Thread.Sleep(50);
                }
            }
        })
        {
            IsBackground = true,
            Name = "CONVERTit.Pipe"
        };
        thread.Start();
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
