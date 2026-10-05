// GameCore.Studio.Etos.Testing - a minimal HTTP/1.1 + WebSocket (RFC 6455) server over TcpListener, for the fake
// companion. It is written against sockets only, so the same code runs under plain dotnet (the client tests) and inside
// the Unity Editor (EditMode tests), where HttpListener's WebSocket support differs between runtimes. One request per
// connection (responses carry "Connection: close"); WebSocket frames from the client are unmasked, server frames are
// sent unmasked, pings are answered, fragmented messages are reassembled. Test infrastructure only.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameCore.Studio.Etos.Testing
{
    /// <summary>One parsed HTTP request.</summary>
    public sealed class MiniRequest
    {
        public MiniRequest(string method, string path, Dictionary<string, string> query, Dictionary<string, string> headers, byte[] body)
        {
            Method = method;
            Path = path;
            Query = query;
            Headers = headers;
            Body = body;
        }

        public string Method { get; }

        /// <summary>The path without the query.</summary>
        public string Path { get; }

        public Dictionary<string, string> Query { get; }

        /// <summary>Header names are lowercase.</summary>
        public Dictionary<string, string> Headers { get; }

        public byte[] Body { get; }

        public string BodyText => Encoding.UTF8.GetString(Body);

        public string? Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out string? value) ? value : null;

        public bool WantsWebSocket => string.Equals(Header("upgrade"), "websocket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The connection a request arrived on: answer it, or upgrade it to a WebSocket.</summary>
    public sealed class MiniConnection
    {
        private readonly Stream _stream;
        private bool _answered;

        internal MiniConnection(Stream stream)
        {
            _stream = stream;
        }

        public async Task RespondAsync(int status, string contentType, byte[] body, IDictionary<string, string>? headers = null)
        {
            if (_answered)
            {
                return;
            }

            _answered = true;
            StringBuilder head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Reason(status)).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            head.Append("Connection: close\r\n");
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                }
            }

            head.Append("\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            await _stream.WriteAsync(headBytes, 0, headBytes.Length).ConfigureAwait(false);
            await _stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }

        public Task RespondJsonAsync(int status, string json)
        {
            return RespondAsync(status, "application/json", Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Completes the WebSocket handshake.</summary>
        public async Task<MiniWebSocket> UpgradeAsync(MiniRequest request)
        {
            string key = request.Header("sec-websocket-key") ?? string.Empty;
            string accept;
            using (SHA1 sha = SHA1.Create())
            {
                accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            }

            _answered = true;
            string head = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(head);
            await _stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
            return new MiniWebSocket(_stream);
        }

        public async Task WaitForDisconnectAsync()
        {
            byte[] buffer = new byte[1];
            while (await _stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false) != 0) { }
            _answered = true;
            return;
        }

        internal bool Answered => _answered;

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 409: return "Conflict";
                case 413: return "Payload Too Large";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                default: return "Status";
            }
        }
    }

    /// <summary>The server side of a WebSocket.</summary>
    public sealed class MiniWebSocket
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _write = new SemaphoreSlim(1, 1);
        private volatile bool _closed;

        internal MiniWebSocket(Stream stream)
        {
            _stream = stream;
        }

        public bool IsClosed => _closed;

        public async Task SendTextAsync(string text)
        {
            await WriteFrameAsync(0x1, Encoding.UTF8.GetBytes(text)).ConfigureAwait(false);

            return;
        }

        /// <summary>The next text message; null once the client closed (or the connection broke).</summary>
        public async Task<string?> ReceiveTextAsync()
        {
            MemoryStream message = new MemoryStream();
            try
            {
                while (true)
                {
                    byte[] head = await ReadExactly(2).ConfigureAwait(false);
                    bool fin = (head[0] & 0x80) != 0;
                    int opcode = head[0] & 0x0f;
                    bool masked = (head[1] & 0x80) != 0;
                    long length = head[1] & 0x7f;
                    if (length == 126)
                    {
                        byte[] ext = await ReadExactly(2).ConfigureAwait(false);
                        length = (ext[0] << 8) | ext[1];
                    }
                    else if (length == 127)
                    {
                        byte[] ext = await ReadExactly(8).ConfigureAwait(false);
                        length = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            length = (length << 8) | ext[i];
                        }
                    }

                    byte[] mask = masked ? await ReadExactly(4).ConfigureAwait(false) : new byte[4];
                    byte[] payload = await ReadExactly((int)length).ConfigureAwait(false);
                    if (masked)
                    {
                        for (int i = 0; i < payload.Length; i++)
                        {
                            payload[i] ^= mask[i % 4];
                        }
                    }

                    switch (opcode)
                    {
                        case 0x8:
                            await CloseAsync().ConfigureAwait(false);
                            return null;
                        case 0x9:
                            await WriteFrameAsync(0xA, payload).ConfigureAwait(false);
                            continue;
                        case 0xA:
                            continue;
                    }

                    message.Write(payload, 0, payload.Length);
                    if (fin)
                    {
                        return Encoding.UTF8.GetString(message.ToArray());
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is SocketException || error is EndOfStreamException)
            {
                _closed = true;
                return null;
            }
        }

        public async Task CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            try
            {
                await WriteFrameAsync(0x8, new byte[] { 0x03, 0xE8 }).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is SocketException)
            {
            }

            _closed = true;
            Abort();
        }

        /// <summary>Drops the TCP connection without a close frame (a broken network).</summary>
        public void Abort()
        {
            _closed = true;
            try
            {
                _stream.Dispose();
            }
            catch (IOException)
            {
            }
        }

        private async Task WriteFrameAsync(int opcode, byte[] payload)
        {
            await _write.WaitAsync().ConfigureAwait(false);
            try
            {
                MemoryStream frame = new MemoryStream();
                frame.WriteByte((byte)(0x80 | opcode));
                if (payload.Length < 126)
                {
                    frame.WriteByte((byte)payload.Length);
                }
                else if (payload.Length <= 0xffff)
                {
                    frame.WriteByte(126);
                    frame.WriteByte((byte)(payload.Length >> 8));
                    frame.WriteByte((byte)(payload.Length & 0xff));
                }
                else
                {
                    frame.WriteByte(127);
                    long length = payload.Length;
                    for (int i = 7; i >= 0; i--)
                    {
                        frame.WriteByte((byte)((length >> (8 * i)) & 0xff));
                    }
                }

                frame.Write(payload, 0, payload.Length);
                byte[] bytes = frame.ToArray();
                await _stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is SocketException)
            {
                _closed = true;
                throw new IOException("The WebSocket is closed.", error);
            }
            finally
            {
                _write.Release();
            }
        }

        private async Task<byte[]> ReadExactly(int count)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await _stream.ReadAsync(buffer, read, count - read).ConfigureAwait(false);
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }

            return buffer;
        }
    }

    /// <summary>The listener: one handler call per request.</summary>
    public sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<MiniRequest, MiniConnection, Task> _handler;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public MiniHttpServer(Func<MiniRequest, MiniConnection, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public int Port { get; private set; }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (error is ObjectDisposedException || error is SocketException || error is InvalidOperationException)
                {
                    return;
                }

                client.NoDelay = true;
                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                MiniConnection connection = new MiniConnection(stream);
                MiniRequest? request;
                try
                {
                    request = await ReadRequest(stream).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is FormatException)
                {
                    return;
                }

                if (request == null)
                {
                    return;
                }

                try
                {
                    await _handler(request, connection).ConfigureAwait(false);
                    if (!connection.Answered)
                    {
                        await connection.RespondJsonAsync(500, "{\"code\":\"internal\",\"message\":\"the fake did not answer\"}").ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException)
                {
                }
                catch (Exception error)
                {
                    try
                    {
                        string body = "{\"code\":\"internal\",\"message\":" + Newtonsoft.Json.JsonConvert.ToString("fake companion fault: " + error.Message) + "}";
                        await connection.RespondJsonAsync(500, body).ConfigureAwait(false);
                    }
                    catch (Exception inner) when (inner is IOException || inner is SocketException || inner is ObjectDisposedException)
                    {
                    }
                }
            }
        }

        private static async Task<MiniRequest?> ReadRequest(Stream stream)
        {
            List<byte> headBytes = new List<byte>();
            byte[] one = new byte[1];
            while (true)
            {
                int n = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n == 0)
                {
                    return null;
                }

                headBytes.Add(one[0]);
                int c = headBytes.Count;
                if (c >= 4 && headBytes[c - 4] == '\r' && headBytes[c - 3] == '\n' && headBytes[c - 2] == '\r' && headBytes[c - 1] == '\n')
                {
                    break;
                }

                if (c > 64 * 1024)
                {
                    throw new FormatException("HTTP head too large.");
                }
            }

            string[] lines = Encoding.ASCII.GetString(headBytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2)
            {
                throw new FormatException("Bad request line.");
            }

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    headers[lines[i].Substring(0, colon).Trim().ToLowerInvariant()] = lines[i].Substring(colon + 1).Trim();
                }
            }

            string target = first[1];
            string path = target;
            Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.Ordinal);
            int q = target.IndexOf('?');
            if (q >= 0)
            {
                path = target.Substring(0, q);
                foreach (string pair in target.Substring(q + 1).Split('&'))
                {
                    if (pair.Length == 0)
                    {
                        continue;
                    }

                    int eq = pair.IndexOf('=');
                    string name = Uri.UnescapeDataString(eq < 0 ? pair : pair.Substring(0, eq));
                    string value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(eq + 1));
                    query[name] = value;
                }
            }

            byte[] body = Array.Empty<byte>();
            if (headers.TryGetValue("content-length", out string? lengthText) && int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int length) && length > 0)
            {
                body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = await stream.ReadAsync(body, read, length - read).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw new IOException("Body truncated.");
                    }

                    read += n;
                }
            }
            else if (headers.TryGetValue("transfer-encoding", out string? encoding) && encoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                body = await ReadChunked(stream).ConfigureAwait(false);
            }

            return new MiniRequest(first[0], path, query, headers, body);
        }

        private static async Task<byte[]> ReadChunked(Stream stream)
        {
            MemoryStream body = new MemoryStream();
            while (true)
            {
                string line = await ReadLine(stream).ConfigureAwait(false);
                int size = int.Parse(line.Split(';')[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    await ReadLine(stream).ConfigureAwait(false);
                    return body.ToArray();
                }

                byte[] chunk = new byte[size];
                int read = 0;
                while (read < size)
                {
                    int n = await stream.ReadAsync(chunk, read, size - read).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw new IOException("Chunk truncated.");
                    }

                    read += n;
                }

                body.Write(chunk, 0, size);
                await ReadLine(stream).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadLine(Stream stream)
        {
            List<byte> bytes = new List<byte>();
            byte[] one = new byte[1];
            while (true)
            {
                int n = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n == 0)
                {
                    throw new IOException("Line truncated.");
                }

                if (one[0] == '\n')
                {
                    break;
                }

                if (one[0] != '\r')
                {
                    bytes.Add(one[0]);
                }
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }
}
