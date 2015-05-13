using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Fleck;

namespace Expresharp.Fleck
{
    /// <summary>
    /// Middleware for processing WebSocket connections.
    /// </summary>
    public class WebSocketMiddleware : IMiddleware
    {
        readonly IEnumerable<String> _subProtocols;
        private Action<IWebSocketConnection> _config;

        /// <summary>
        /// Initializes.
        /// </summary>
        /// <param name="subProtocols">sub protocols</param>
        public WebSocketMiddleware(IEnumerable<String> subProtocols = null)
        {
            _subProtocols = subProtocols ?? new String[0];
        }

        /// <inheritdoc/>
        public void Handle(IHttpRequest req, IHttpResponse res, Next next)
        {
            if (!req.IsWebSocketRequest())
            {
                next();
                return;
            }

            WebSocketContext ctx = new WebSocketContext(req, res, _config, _subProtocols);
            ctx.Connection.StartReceiving();
        }

        /// <summary>
        /// Sets up a callback to configure events of new incoming WebSocket connections.
        /// </summary>
        /// <param name="config"></param>
        /// <returns>self for chaining</returns>
        public WebSocketMiddleware OnConnection(Action<IWebSocketConnection> config)
        {
            _config = config;
            return this;
        }

        private static WebSocketHttpRequest FromRequest(IHttpRequest req)
        {
            WebSocketHttpRequest wsReq = new WebSocketHttpRequest()
            {
                Method = req.HttpMethod,
                Path = req.Url.AbsolutePath,
                Scheme = req.Url.Scheme
            };

            foreach (var key in req.Headers.AllKeys)
            {
                wsReq.Headers[key] = req.Headers[key];
            }

            return wsReq;
        }

        class WebSocketContext : ISocket
        {
            static readonly Byte[] EmptyBytes = new Byte[0];
            readonly IHttpRequest _request;
            readonly IHttpResponse _response;
            readonly WebSocketConnection _connection;
            private Boolean _close;

            public WebSocketContext(IHttpRequest req, IHttpResponse res, Action<IWebSocketConnection> config, IEnumerable<String> subProtocols)
            {
                _request = req;
                _response = res;

                _connection = new WebSocketConnection(this, config, data => FromRequest(req),
                    r => HandlerFactory.BuildHandler(r,
                        s => _connection.OnMessage(s),
                        _connection.Close,
                        b => _connection.OnBinary(b),
                        b => _connection.OnPing(b),
                        b => _connection.OnPong(b)),
                    s => SubProtocolNegotiator.Negotiate(subProtocols, s));
                _connection.CreateHandler(EmptyBytes);
            }

            public WebSocketConnection Connection
            {
                get { return _connection; }
            }

            Boolean ISocket.Connected
            {
                get { return !_close; }
            }

            String ISocket.RemoteIpAddress
            {
                get
                {
                    var ep = _request.RemoteEndPoint;
                    return ep != null ? ep.Address.ToString() : null;
                }
            }

            Int32 ISocket.RemotePort
            {
                get
                {
                    var ep = _request.RemoteEndPoint;
                    return ep != null ? ep.Port : -1;
                }
            }

            Stream ISocket.Stream
            {
                get { return _request.InputStream ?? _response.OutputStream; }
            }

            Task ISocket.Send(Byte[] buffer, Action callback, Action<Exception> error)
            {
                return _response.OutputStream.Send(buffer, callback, error);
            }

            Task<Int32> ISocket.Receive(Byte[] buffer, Action<Int32> callback, Action<Exception> error, Int32 offset)
            {
                return _request.InputStream.Receive(buffer, offset, buffer.Length, callback, error);
            }

            void ISocket.Dispose()
            {
                _response.Dispose();
            }

            void ISocket.Close()
            {
                _response.End();
                _close = true;
            }

            Task ISocket.Authenticate(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, Action callback, Action<Exception> error)
            {
                throw new NotImplementedException();
            }

            Task<ISocket> ISocket.Accept(Action<ISocket> callback, Action<Exception> error)
            {
                throw new NotImplementedException();
            }

            Boolean ISocket.NoDelay
            {
                get { throw new NotImplementedException(); }
                set { throw new NotImplementedException(); }
            }

            void ISocket.Bind(EndPoint ipLocal)
            {
                throw new NotImplementedException();
            }

            void ISocket.Listen(int backlog)
            {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// Extensions for <see cref="Express"/>.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Accepts WebSocket connections on this app.
        /// </summary>
        /// <param name="app">the <see cref="Express"/> instance</param>
        /// <param name="config">a callback to configure events of new incoming WebSocket connections</param>
        /// <param name="subProtocols">sub protocols</param>
        /// <returns>self for chaining</returns>
        public static Express WebSocket(this Express app, Action<IWebSocketConnection> config, IEnumerable<String> subProtocols = null)
        {
            var ws = new WebSocketMiddleware(subProtocols);
            ws.OnConnection(config);
            app.Use(ws);
            return app;
        }

        /// <summary>
        /// Accepts WebSocket connections on this app.
        /// </summary>
        /// <param name="app">the <see cref="Express"/> instance</param>
        /// <param name="path">the route path where to accept WebSocket connections</param>
        /// <param name="config">a callback to configure events of new incoming WebSocket connections</param>
        /// <param name="subProtocols">sub protocols</param>
        /// <returns>self for chaining</returns>
        public static Express WebSocket(this Express app, String path, Action<IWebSocketConnection> config, IEnumerable<String> subProtocols = null)
        {
            var ws = new WebSocketMiddleware(subProtocols);
            ws.OnConnection(config);
            app.Use(path, ws);
            return app;
        }

        internal static Task Send(this Stream stream, Byte[] bytes, Action callback, Action<Exception> error)
        {
            try
            {
                Func<AsyncCallback, Object, IAsyncResult> begin =
                    (cb, s) => stream.BeginWrite(bytes, 0, bytes.Length, cb, s);

                Task task = Task.Factory.FromAsync(begin, stream.EndWrite, null);
                task.ContinueWith(t => callback(), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

                return task;
            }
            catch (Exception e)
            {
                error(e);
                return null;
            }
        }

        internal static Task<Int32> Receive(this Stream stream, Byte[] buffer, Int32 offset, Int32 count, Action<Int32> callback, Action<Exception> error)
        {
            try
            {
                Func<AsyncCallback, Object, IAsyncResult> begin =
                    (cb, s) => stream.BeginRead(buffer, offset, count, cb, s);

                Task<Int32> task = Task.Factory.FromAsync<Int32>(begin, stream.EndRead, null);
                task.ContinueWith(t => callback(t.Result), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                return task;
            }
            catch (Exception e)
            {
                error(e);
                return null;
            }
        }
    }
}
