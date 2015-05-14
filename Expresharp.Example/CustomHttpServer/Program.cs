using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Expresharp;
using Expresharp.Fleck;

namespace CustomHttpServer
{
    /// <summary>
    /// Instead of <see cref="System.Net.HttpListener"/>, you may want to
    /// customize your own HTTP server. This is quite simple with Expresharp.
    /// Just wrap your requests/responses and push them into an Expresharp app
    /// whenever you like.
    /// 
    /// In this example, we build an Expresharp app on an HTTP listener from
    /// a 3rd-party package: websocket-sharp.
    /// This package allows normal HTTP requests and websockets running together,
    /// however kind of buggy (no offence, my Firefox said that).
    /// Another package, Fleck, works fine with websockets, but gives up
    /// usual HTTP requests.
    /// 
    /// Here in Expresharp, we combine these two great packages together.
    /// 
    /// The <see cref="System.Net.HttpListener"/> accepts WebSocket only on
    /// .NET 4.5+ and Windows 8+, where that would be a better option.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // prepare an Express app
            var app = new Express();

            // same as usual
            app.Get("/", (req, res) => res.Send("Hello World!"));

            // set up a echo WebSocket handler on path "/echo"
            var echo = new WebSocketMiddleware();
            app.Use("/echo", echo);

            // configure events of incoming websocket
            echo.OnConnection(ws =>
            {
                ws.OnOpen = () => Console.WriteLine("Opened: {0}:{1}", ws.ConnectionInfo.ClientIpAddress, ws.ConnectionInfo.ClientPort);
                ws.OnClose = () => Console.WriteLine("Closed: {0}:{1}", ws.ConnectionInfo.ClientIpAddress, ws.ConnectionInfo.ClientPort);
                ws.OnError = e => Console.WriteLine("Error: {0}", e);
                ws.OnMessage = msg =>
                {
                    Console.WriteLine("Received: {0}", msg);
                    ws.Send("Echo: " + msg);
                };
            });

            // start a listener from WebSocketSharp
            var server = new WebSocketSharp.Net.HttpListener();
            server.Prefixes.Add("http://localhost:8080/");
            server.Start();

            Console.WriteLine("Example app listening at {0}.", server.Prefixes.First());

            while (server.IsListening)
            {
                // get a incoming request
                var ctx = server.GetContext();

                // wrap request/response
                var req = new HttpListenerRequestWrapper(ctx.Request);
                var res = new HttpListenerResponseWrapper(ctx.Response);

                // extra works are needed if this is a WebSocket request
                if (req.IsWebSocketRequest())
                {
                    var wsCtx = ctx.AcceptWebSocket(null);
                    req.WebSocketContext = wsCtx;
                    res.WebSocketContext = wsCtx;
                }

                // push req/res into the Express app, it will take good care of them
                app.Handle(req, res, null);
            }
        }
    }

    /// <summary>
    /// Wraps a <see cref="WebSocketSharp.Net.HttpListenerRequest"/> as an <see cref="IHttpRequest"/>.
    /// </summary>
    class HttpListenerRequestWrapper : IHttpRequest
    {
        readonly WebSocketSharp.Net.HttpListenerRequest _request;
        readonly IDictionary<Object, Object> _params = new Dictionary<Object, Object>();
        private Stream _stream;

        public HttpListenerRequestWrapper(WebSocketSharp.Net.HttpListenerRequest request)
        {
            _request = request;
        }

        public WebSocketSharp.Net.WebSockets.HttpListenerWebSocketContext WebSocketContext
        {
            set
            {
                // sorry for the break in :(, but the original underlying stream
                // would be needed for reading/writing of WebSocket connection
                _stream = (Stream)value.GetType().GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)
                    .First(p => p.PropertyType.Equals(typeof(Stream))).GetValue(value, null);
            }
        }

        public WebSocketSharp.Net.HttpListenerRequest Request
        {
            get { return _request; }
        }

        public Version Version
        {
            get { return _request.ProtocolVersion; }
        }

        public String HttpMethod
        {
            get { return _request.HttpMethod; }
        }

        public String RawUrl
        {
            get { return _request.RawUrl; }
        }

        public Uri Url
        {
            get { return _request.Url; }
        }

        public String RouteUrl { get; set; }

        public String BaseUrl { get; set; }

        public NameValueCollection Headers
        {
            get { return _request.Headers; }
        }

        public Stream InputStream
        {
            get { return _stream ?? _request.InputStream; }
        }

        public IPEndPoint RemoteEndPoint
        {
            get { return _request.RemoteEndPoint; }
        }

        public String UserHostName
        {
            get { return _request.UserHostName; }
        }

        public void Params(Object key, Object value)
        {
            _params[key] = value;
        }

        public Object Params(Object key)
        {
            Object ret;
            return _params.TryGetValue(key, out ret) ? ret : null;
        }

        public T ParamsAs<T>(Object key, T def = default(T))
        {
            Object result = Params(key);
            return result != null ? (T)result : def;
        }
    }

    /// <summary>
    /// Wraps a <see cref="WebSocketSharp.Net.HttpListenerResponse"/> as an <see cref="IHttpResponse"/>.
    /// </summary>
    class HttpListenerResponseWrapper : IHttpResponse
    {
        readonly WebSocketSharp.Net.HttpListenerResponse _response;
        private Stream _stream;

        public HttpListenerResponseWrapper(WebSocketSharp.Net.HttpListenerResponse response)
        {
            _response = response;
        }

        public WebSocketSharp.Net.WebSockets.HttpListenerWebSocketContext WebSocketContext
        {
            set
            {
                // sorry for the break in too :(, but the original underlying stream
                // would be needed for reading/writing of WebSocket connection
                _stream = (Stream)value.GetType().GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)
                    .First(p => p.PropertyType.Equals(typeof(Stream))).GetValue(value, null);
            }
        }

        public Int32 StatusCode
        {
            get { return _response.StatusCode; }
            set
            {
                ClearWebSocketContext();
                _response.StatusCode = value;
            }
        }

        public String StatusDescription
        {
            get { return _response.StatusDescription; }
            set
            {
                ClearWebSocketContext();
                _response.StatusDescription = value;
            }
        }

        public Stream OutputStream
        {
            get { return _stream ?? _response.OutputStream; }
        }

        public IHttpResponse Status(Int32 status)
        {
            StatusCode = status;
            return this;
        }

        public IHttpResponse SetHeader(String key, String val)
        {
            _response.Headers[key] = val;
            return this;
        }

        public void Redirect(String url)
        {
            ClearWebSocketContext();
            _response.Redirect(url);
        }

        public void Send(String body)
        {
            ClearWebSocketContext();
            if (_response.ContentType == null)
                _response.ContentType = "text/html";
            using (StreamWriter w = new StreamWriter(_response.OutputStream))
            {
                w.Write(body);
            }
        }

        public void End()
        {
            if (_response != null)
                _response.Close();
            if (_stream != null)
                _stream.Close();
        }

        public void Dispose()
        {
            if (_response != null)
                ((IDisposable)_response).Dispose();
            if (_stream != null)
                _stream.Dispose();
        }

        /// <summary>
        /// Clears context of WebSocket so that this response could act like a normal one.
        /// </summary>
        private void ClearWebSocketContext()
        {
            _stream = null;
        }
    }
}
