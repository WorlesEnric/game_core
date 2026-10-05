// GameCore.Studio.Etos.Client - JSON text frames over System.Net.WebSockets (both the companion's WebSockets send and
// receive only text frames; pings are answered by the WebSocket implementation).
#nullable enable
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Text-frame helpers.</summary>
    public static class WebSocketText
    {
        /// <summary>Largest text message accepted (an event frame carrying a full request view stays far below).</summary>
        public const int MaxMessageBytes = 8 * 1024 * 1024;

        /// <summary>The next complete text message, or null when the peer closed the socket.</summary>
        public static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken ct)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream message = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.CloseReceived)
                        {
                            try
                            {
                                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (WebSocketException)
                            {
                            }
                        }

                        return null;
                    }

                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        throw EtosException.Protocol("A WebSocket message exceeded " + MaxMessageBytes + " bytes.");
                    }

                    if (result.EndOfMessage)
                    {
                        if (result.MessageType != WebSocketMessageType.Text)
                        {
                            message.SetLength(0);
                            continue;
                        }

                        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    }
                }
            }
        }

        /// <summary>Sends one text message.</summary>
        public static Task SendAsync(WebSocket socket, string text, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        /// <summary>Closes politely (bounded), then disposes.</summary>
        public static async Task CloseAsync(WebSocket socket, TimeSpan within)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    using (CancellationTokenSource deadline = new CancellationTokenSource(within))
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, deadline.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception error) when (error is WebSocketException || error is OperationCanceledException || error is ObjectDisposedException || error is IOException)
            {
            }
            finally
            {
                socket.Dispose();
            }

            return;
        }
    }
}
