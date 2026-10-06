using Microsoft.Web.WebView2.Core;

namespace FourFoldAccountManager.Desktop.Services.LiveFeed;

// Watches one panel's sockets through the DevTools protocol, read-only. It subscribes to exactly three events: a socket
// being created, a frame being received, a socket closing. Sent frames (which carry the login) are never subscribed
// to, and nothing here can send, change or intercept traffic. The handlers run on the UI thread, which draws the game
// panels, so they only queue the event's JSON; the feed's worker does all the parsing.
internal sealed class GameFeedTap(CoreWebView2 core, Guid accountId, Action<TapEvent> enqueue, TimeProvider clock)
{
    // Small buffers: the feed never reads HTTP bodies, so the browser needn't keep them for DevTools.
    private const string EnableParameters = """{"maxTotalBufferSize":1024,"maxResourceBufferSize":1024}""";

    private CoreWebView2DevToolsProtocolEventReceiver? _created;
    private CoreWebView2DevToolsProtocolEventReceiver? _frames;
    private CoreWebView2DevToolsProtocolEventReceiver? _closed;
    private bool _detached;

    public async Task AttachAsync()
    {
        // Subscribed before Network.enable, so a socket that opens at once isn't missed.
        _created = core.GetDevToolsProtocolEventReceiver("Network.webSocketCreated");
        _frames = core.GetDevToolsProtocolEventReceiver("Network.webSocketFrameReceived");
        _closed = core.GetDevToolsProtocolEventReceiver("Network.webSocketClosed");
        _created.DevToolsProtocolEventReceived += OnCreated;
        _frames.DevToolsProtocolEventReceived += OnFrame;
        _closed.DevToolsProtocolEventReceived += OnClosed;
        await core.CallDevToolsProtocolMethodAsync("Network.enable", EnableParameters);
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        if (_created is not null)
        {
            _created.DevToolsProtocolEventReceived -= OnCreated;
        }

        if (_frames is not null)
        {
            _frames.DevToolsProtocolEventReceived -= OnFrame;
        }

        if (_closed is not null)
        {
            _closed.DevToolsProtocolEventReceived -= OnClosed;
        }

        _ = DisableAsync();
    }

    private async Task DisableAsync()
    {
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
        }
        catch (Exception)
        {
            // The view is being disposed; there's nothing left to disable.
        }
    }

    private void OnCreated(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        Queue(TapEventKind.SocketCreated, args);

    private void OnFrame(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        Queue(TapEventKind.Frame, args);

    private void OnClosed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        Queue(TapEventKind.SocketClosed, args);

    private void Queue(TapEventKind kind, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (!_detached)
        {
            enqueue(new TapEvent(accountId, kind, args.ParameterObjectAsJson, clock.GetUtcNow()));
        }
    }
}
