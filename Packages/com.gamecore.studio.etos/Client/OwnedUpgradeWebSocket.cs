#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Owns the upgrade connection on Mono, whose ClientWebSocket does not close a pending HTTP upgrade.
    /// The framework still implements all WebSocket framing, masking, ping and close handling.</summary>
    internal sealed class OwnedUpgradeWebSocket : WebSocket
    {
        private readonly TcpClient _connection;
        private readonly WebSocket _socket;
        private OwnedUpgradeWebSocket(TcpClient connection, WebSocket socket) { _connection = connection; _socket = socket; }

        public static async Task<WebSocket> ConnectAsync(Uri uri, string projectId, CancellationToken token)
        {
            var connection = new TcpClient();
            bool transferred = false;
            Stream? stream = null;
            try
            {
                using (token.Register(() => connection.Dispose()))
                {
                    token.ThrowIfCancellationRequested();
                    await connection.ConnectAsync(uri.DnsSafeHost, uri.Port).ConfigureAwait(false);
                    stream = connection.GetStream();
                    if (uri.Scheme == "wss")
                    {
                        // Default platform certificate/hostname validation; no permissive callback.
                        var tls = new SslStream(stream, false);
                        stream = tls;
                        await tls.AuthenticateAsClientAsync(uri.DnsSafeHost).ConfigureAwait(false);
                    }
                    byte[] nonce = new byte[16];
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
                    string key = Convert.ToBase64String(nonce);
                    string headers = "GET " + uri.PathAndQuery + " HTTP/1.1\r\nHost: " + uri.Authority
                        + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: " + key
                        + "\r\nX-GameCore-Project: " + projectId + "\r\n\r\n";
                    byte[] request = Encoding.ASCII.GetBytes(headers);
                    await stream.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                    string response = await ReadHeaders(stream, token).ConfigureAwait(false);
                    ValidateHeaders(response, key);
                    token.ThrowIfCancellationRequested();
                    WebSocket framed = WebSocket.CreateFromStream(stream, false, null, TimeSpan.FromSeconds(20));
                    transferred = true;
                    return new OwnedUpgradeWebSocket(connection, framed);
                }
            }
            finally
            {
                if (!transferred) { stream?.Dispose(); connection.Dispose(); }
            }
        }

        private static async Task<string> ReadHeaders(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (bytes.Count < 16 * 1024)
            {
                if (await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false) == 0)
                    throw EtosException.Protocol("The WebSocket upgrade closed before its headers completed.");
                bytes.Add(one[0]);
                int count = bytes.Count;
                if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10)
                    return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw EtosException.Protocol("The WebSocket upgrade headers exceeded 16 KiB.");
        }

        private static void ValidateHeaders(string response, string key)
        {
            string[] lines = response.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] status = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string name = lines[i].Substring(0, colon).Trim();
                    if (headers.ContainsKey(name)) throw EtosException.Protocol("Duplicate WebSocket upgrade header.");
                    headers[name] = lines[i].Substring(colon + 1).Trim();
                }
            }
            string accept;
            using (SHA1 hash = SHA1.Create())
                accept = Convert.ToBase64String(hash.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            if (status.Length < 2 || status[0] != "HTTP/1.1" || status[1] != "101"
                || !headers.TryGetValue("Upgrade", out string? upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
                || !headers.TryGetValue("Connection", out string? connection) || !HasUpgrade(connection)
                || !headers.TryGetValue("Sec-WebSocket-Accept", out string? actual) || actual != accept
                || headers.ContainsKey("Sec-WebSocket-Extensions") || headers.ContainsKey("Sec-WebSocket-Protocol"))
                throw EtosException.Protocol("The node refused or returned an invalid WebSocket upgrade.");
        }

        private static bool HasUpgrade(string value)
        {
            foreach (string part in value.Split(',')) if (part.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public override WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
        public override string? CloseStatusDescription => _socket.CloseStatusDescription;
        public override string? SubProtocol => _socket.SubProtocol;
        public override WebSocketState State => _socket.State;
        public override void Abort() { _connection.Dispose(); _socket.Abort(); }
        public override void Dispose() { _connection.Dispose(); _socket.Dispose(); }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => _socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => _socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => _socket.ReceiveAsync(buffer, cancellationToken);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }
}
