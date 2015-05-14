# Expresharp - Express on .NET

Expresharp is a [single file] (https://github.com/longshine/expresharp/blob/master/Expresharp/Express.cs)
that can be drop in to your project and enable coding like [Express.js] (http://expressjs.com) for web application.

Express.js is a:

> Fast, unopinionated, minimalist web framework for [node] (http://nodejs.org).

Have fun!

```csharp
var app = new Express();

app.Get("/", (req, res) => res.Send("Hello World!"));

var server = app.Listen(3000);
```

## Getting started

### Installation

Expresharp is available on [NuGet] (http://www.nuget.org/packages/Expresharp/).

```powershell
PM> Install-Package Expresharp
```

### Hello world

Here is an example of a very basic Express app.
It starts a server and listens on port 3000 for connection.
It will respond with “Hello World!” for requests to the homepage.
For every other path, it will respond with a **404 Not Found**.

[`view code`] (https://github.com/longshine/expresharp/blob/master/Expresharp.Example/HelloWorld/Program.cs)

```csharp
using System;
using System.Linq;
using Expresharp;

class Program
{
  static void Main(string[] args)
  {
    var app = new Express();
    
    app.Get("/", (req, res) => res.Send("Hello World!"));
    
    var server = app.Listen(3000);
    
    Console.WriteLine("Example app listening at {0}.", server.Prefixes.First());
    Console.WriteLine("Press ENTER to exit.");
    Console.ReadLine();
  }
}
```

### Basic routing

Routing refers to determining how an application responds to a client request
to a particular endpoint, which is a URI (or path) and a specific HTTP request
method (GET, POST, and so on).
Each route can have one or more handler functions, which is / are executed
when the route is matched.

[`view code`] (https://github.com/longshine/expresharp/blob/master/Expresharp.Example/BasicRouting/Program.cs)

```csharp
// respond with "Hello World!" on the homepage
app.Get("/", (req, res) => res.Send("Hello World!"));

// accept POST request on the homepage
app.Post("/", (req, res) => res.Send("Got a POST request"));

// accept PUT request at /user
app.Put("/user", (req, res) => res.Send("Got a PUT request at /user"));

// accept DELETE request at /user
app.Delete("/user", (req, res) => res.Send("Got a DELETE request at /user"));
```

### Serving static files

Serving files, such as images, CSS, JavaScript and other static files
is accomplished with the help of a built-in middleware - `Express.Static`.

[`view code`] (https://github.com/longshine/expresharp/blob/master/Expresharp.Example/StaticFiles/Program.cs)

```csharp
app.Use(Express.Static("public"));
```

Now, you will be able to load the files under the `public` directory:

```
http://localhost:3000/css/style.css
http://localhost:3000/js/app.js
http://localhost:3000/hello.html
```

By specifying a mount path for the static directory, as shown below,
you can create a "virtual" path prefix for those files.

```csharp
app.Use("/static", Express.Static("public"));
```

Now, you will be able to load the files under the `public` directory,
from the path prefix "/static".

```
http://localhost:3000/static/css/style.css
http://localhost:3000/static/js/app.js
http://localhost:3000/static/hello.html
```

For more docs please refer to [Guide] (http://expressjs.com/guide/routing.html)
or [API reference] (http://expressjs.com/4x/api.html)
on [Express.js] (http://expressjs.com) website.

## WebSocket

[Expresharp.Fleck] (https://github.com/longshine/expresharp/blob/master/Expresharp.Fleck/WebSocketMiddleware.cs)
is a middleware of Express which enables WebSocket ability by [Fleck] (https://github.com/statianzo/Fleck).

### Installation

Expresharp.Fleck is available on [NuGet] (http://www.nuget.org/packages/Expresharp.Fleck/).

```powershell
PM> Install-Package Expresharp
```

### Usage

[`view code`] (https://github.com/longshine/expresharp/blob/master/Expresharp.Example/WebSocketDemo/Program.cs)

It's easy to add WebSocket handler to a specified path or globally with extension methods.

```csharp
app.WebSocket("/echo", ws =>
{
  ws.OnOpen = () => Console.WriteLine("Opened");
  ws.OnClose = () => Console.WriteLine("Closed");
  ws.OnError = e => Console.WriteLine("Error: {0}", e);
  ws.OnMessage = msg =>
  {
    Console.WriteLine("Received: {0}", msg);
    ws.Send("Echo: " + msg);
  };
});
```

Or create an instance of the `WebSocketMiddleware` for reuse:

```csharp
var echo = new WebSocketMiddleware();

echo.OnConnection(ws =>
{
  ws.OnOpen = () => Console.WriteLine("Opened");
  ws.OnClose = () => Console.WriteLine("Closed");
  ws.OnError = e => Console.WriteLine("Error: {0}", e);
  ws.OnMessage = msg =>
  {
    Console.WriteLine("Received: {0}", msg);
    ws.Send("Echo: " + msg);
  };
});

app.Use("/echo", echo);
app.Use("/chat", echo);
```

### Note

The `System.Net.HttpListener` inside Expresharp accepts WebSocket connections
only on .NET 4.5+ and Windows 8+. For other platforms, a better choice
is to adapt Expresharp to a 3rd party or your own HTTP server.
Check out next section and [Custom HTTP server]
(https://github.com/longshine/expresharp/blob/master/Expresharp.Example/CustomHttpServer/Program.cs)
example for more.

## Custom HTTP server

Instead of `System.Net.HttpListener`, you may like to customize your
own HTTP server. This is quite simple with Expresharp.

There are two interfaces in your concern: `IHttpRequest` and `IHttpResponse`.
Whenever you like, wrap your requests/responses as these two interfaces,
push them into an Expresharp app, and let Express take care of the rest.

[`view code`] (https://github.com/longshine/expresharp/blob/master/Expresharp.Example/CustomHttpServer/Program.cs)

```csharp
class CustomHttpRequest : IHttpRequest { }
class CustomHttpResponse : IHttpResponse { }

// here comes your request/response, wrap them up!
var req = new CustomHttpRequest(request);
var res = new CustomHttpResponse(response);

// push req/res into the Express app, and that's all :)
app.Handle(req, res, null);
```

The last parameter of `app.Handle(IHttpRequest, IHttpResponse, Next)`
is a handler that will receive all requests not consumed by any route,
such as errors and 404s. For most cases, a `null` is enough.

## License

[MIT] (https://github.com/longshine/expresharp/blob/master/LICENSE)
