using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Expresharp.Fleck;

namespace Expresharp.Example.WebSocketDemo
{
    /// <summary>
    /// The <see cref="System.Net.HttpListener"/> does not support WebSocket
    /// unless it is on .NET 4.5+ and Windows 8+,
    /// so we have to run Express on other HTTP servers featuring WebSocket.
    /// In this example, we create a small one for demo purpose.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            var app = new Express();

            // same as usual
            app.Get("/", (req, res) => res.Send("Hello World!"));

            // set up a echo WebSocket handler on path "/echo"
            var echo = new WebSocketMiddleware();
            app.Use("/echo", echo);

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

            // start a custom HTTP server and pass request/response pair to Express app
            var server = new HttpServer(IPAddress.Any, 8080);
            server.Start((req, res) => app.Handle(req, res, null));

            Console.WriteLine("Example app listening at {0}.", server.LocalEndPoint);
            Console.WriteLine("Press ENTER to exit.");
            Console.ReadLine();
        }

        /// <summary>
        /// A tiny and buggy HTTP server.
        /// </summary>
        class HttpServer
        {
            readonly IPEndPoint _ep;
            readonly TcpListener _listener;
            Action<IHttpRequest, IHttpResponse> _handler;

            public HttpServer(IPAddress addr, Int32 port)
            {
                _ep = new IPEndPoint(addr, port);
                _listener = new TcpListener(_ep);
            }

            public TcpListener Listener
            {
                get { return _listener; }
            }

            public IPEndPoint LocalEndPoint
            {
                get { return _ep; }
            }

            public void Start(Action<IHttpRequest, IHttpResponse> handler)
            {
                _handler = handler;
                _listener.Start();
                BeginAccept();
            }

            private void BeginAccept()
            {
                _listener.BeginAcceptSocket(EndAccept, null);
            }

            private void EndAccept(IAsyncResult ar)
            {
                Socket socket = _listener.EndAcceptSocket(ar);
                ThreadPool.QueueUserWorkItem(Process, socket);
                BeginAccept();
            }

            private void Process(Object state)
            {
                var socket = (Socket)state;
                var input = new NetworkStream(socket);
                var output = input;

                var line = ReadLine(input);
                String[] tokens;
                if (String.IsNullOrEmpty(line) ||
                    (tokens = line.Split(new Char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Length != 3)
                {
                    BadRequest(socket, output);
                    return;
                }

                // parse request
                var req = new HttpRequestImpl();
                req.HttpMethod = tokens[0].Trim().ToUpperInvariant();
                req.RawUrl = tokens[1].Trim();
                req.Version = String.Equals("HTTP/1.1", tokens[2]) ? HttpVersion.Version11 : HttpVersion.Version10;

                // read headers
                var headers = new NameValueCollection();
                String host = null;
                while ((line = ReadLine(input)) != null && line.Length > 0)
                {
                    tokens = line.Split(new Char[] { ':' }, 2);
                    if (tokens.Length != 2)
                        // ignore
                        continue;
                    var val = tokens[1].Trim();
                    headers[tokens[0]] = val;
                    if (String.Equals("Host", tokens[0], StringComparison.OrdinalIgnoreCase))
                        host = val;
                }

                req.Headers = headers;
                req.UserHostName = host;

                Uri uri;
                try
                {
                    int port = _ep.Port;
                    if (host == null)
                    {
                        host = _ep.Address.ToString();
                    }
                    else if ((tokens = host.Split(':')).Length == 2)
                    {
                        host = tokens[0];
                        port = Int32.Parse(tokens[1]);
                    }

                    if (req.RawUrl.StartsWith("http://") || req.RawUrl.StartsWith("https://"))
                    {
                        uri = new Uri(req.RawUrl);
                        if (!String.Equals(uri.Host, host))
                        {
                            UriBuilder ub = new UriBuilder(uri);
                            ub.Host = host;
                            uri = ub.Uri;
                        }
                    }
                    else
                    {
                        UriBuilder ub = new UriBuilder("http", host, port, req.RawUrl);
                        
                        uri = ub.Uri;
                    }
                }
                catch (Exception)
                {
                    BadRequest(socket, output);
                    return;
                }

                req.Url = uri;
                req.RemoteEndPoint = (IPEndPoint)socket.RemoteEndPoint;
                req.InputStream = input;

                var res = new HttpResponseImpl(req, socket, output);

                if (_handler != null)
                    _handler(req, res);
            }

            static String ReadLine(Stream stream)
            {
                Int32 b;
                StringBuilder sb = new StringBuilder();

                while (true)
                {
                    b = stream.ReadByte();
                    if (b < 0) break;
                    else if (b == '\n') break;
                    else if (b == '\r') continue;
                    sb.Append(Convert.ToChar(b));
                }

                return sb.ToString();
            }

            static void BadRequest(Socket socket, Stream stream)
            {
                Close(socket, stream, "HTTP/1.0 400 Bad Request\r\nConnection: close\r\n\r\n");
            }

            static void Close(Socket socket, Stream stream, String reason)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(reason);
                stream.Write(bytes, 0, bytes.Length);
                socket.Close();
            }

            class HttpRequestImpl : IHttpRequest
            {
                readonly IDictionary<object, object> _params = new Dictionary<object, object>();

                public Version Version { get; set; }

                public string HttpMethod { get; set; }

                public string RawUrl { get; set; }

                public Uri Url { get; set; }

                public NameValueCollection Headers { get; set; }

                public Stream InputStream { get; set; }

                public IPEndPoint RemoteEndPoint { get; set; }

                public string UserHostName { get; set; }

                public string RouteUrl { get; set; }

                public string BaseUrl { get; set; }

                public void Params(object key, object value)
                {
                    _params[key] = value;
                }

                public object Params(object key)
                {
                    object ret;
                    return _params.TryGetValue(key, out ret) ? ret : null;
                }

                public T ParamsAs<T>(object key, T def = default(T))
                {
                    object result = Params(key);
                    return result != null ? (T)result : def;
                }
            }

            class HttpResponseImpl : IHttpResponse
            {
                readonly HttpRequestImpl _request;
                readonly Socket _socket;
                Stream _raw;
                Stream _wrapped;
                int _statusCode;
                string _statusDescription;

                public HttpResponseImpl(HttpRequestImpl request, Socket socket, Stream stream)
                {
                    _socket = socket;
                    _request = request;
                    _raw = stream;
                    _wrapped = new ResponseStream(stream, this);
                    Headers = new WebHeaderCollection();
                }

                internal HttpRequestImpl Request
                {
                    get { return _request; }
                }

                internal bool HeaderSent { get; set; }

                public int StatusCode
                {
                    get { return _statusCode; }
                    set
                    {
                        ClearWebSocketContext();
                        _statusCode = value;
                    }
                }

                public string StatusDescription
                {
                    get { return _statusDescription; }
                    set
                    {
                        ClearWebSocketContext();
                        _statusDescription = value;
                    }
                }

                public WebHeaderCollection Headers { get; private set; }

                public Stream OutputStream
                {
                    get { return _raw ?? _wrapped; }
                }

                public IHttpResponse Status(int status)
                {
                    StatusCode = status;
                    return this;
                }

                public IHttpResponse SetHeader(string name, string val)
                {
                    Headers[name] = val;
                    return this;
                }

                public void Redirect(string url)
                {
                    StatusCode = 302;
                    Headers["Location"] = url;
                    End();
                }

                public void Send(string body)
                {
                    ClearWebSocketContext();
                    if (Headers["Content-Type"] == null)
                        Headers["Content-Type"] = "text/html";
                    using (StreamWriter w = new StreamWriter(_wrapped))
                    {
                        w.Write(body);
                    }
                }

                public void End()
                {
                    if (_raw != null)
                        _raw.Close();
                    else if (_wrapped != null)
                        _wrapped.Close();
                    _socket.Close();
                }

                public void Dispose()
                {
                    if (_raw != null)
                        _raw.Dispose();
                    else if (_wrapped != null)
                        _wrapped.Dispose();
                    _socket.Dispose();
                }

                private void ClearWebSocketContext()
                {
                    _raw = null;
                }
            }

            class ResponseStream : Stream
            {
                readonly Stream _stream;
                readonly HttpResponseImpl _response;
                bool _disposed;

                public ResponseStream(Stream stream, HttpResponseImpl res)
                {
                    _stream = stream;
                    _response = res;
                }

                public override void Write(byte[] buffer, int offset, int count)
                {
                    if (!_response.HeaderSent)
                        SendHeader();

                    _stream.Write(buffer, offset, count);
                }

                public override void Close()
                {
                    if (_disposed)
                        return;

                    _disposed = true;

                    if (!_response.HeaderSent)
                        SendHeader();

                    _stream.Close();
                    _response.End();
                    base.Close();
                }

                private void SendHeader()
                {
                    var resp = Encoding.UTF8.GetBytes(String.Format("HTTP/{0} {1} {2}\r\n",
                        _response.Request.Version, _response.StatusCode, _response.StatusDescription));
                    _stream.Write(resp, 0, resp.Length);
                    var headers = _response.Headers.ToByteArray();
                    _stream.Write(headers, 0, headers.Length);
                    _response.HeaderSent = true;
                }

                public override void Flush()
                {
                    _stream.Flush();
                }

                public override bool CanRead
                {
                    get { return _stream.CanRead; }
                }

                public override bool CanSeek
                {
                    get { return _stream.CanSeek; }
                }

                public override bool CanWrite
                {
                    get { return _stream.CanWrite; }
                }

                public override long Length
                {
                    get { return _stream.Length; }
                }

                public override long Position
                {
                    get { return _stream.Position; }
                    set { _stream.Position = value; }
                }

                public override int Read(byte[] buffer, int offset, int count)
                {
                    throw new NotSupportedException();
                }

                public override long Seek(long offset, SeekOrigin origin)
                {
                    throw new NotSupportedException();
                }

                public override void SetLength(long value)
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}
