using System;
using System.Linq;
using Expresharp;

namespace Expresharp.Example.BasicRouting
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new Express();

            // respond with "Hello World!" on the homepage
            app.Get("/", (req, res) => res.Send("Hello World!"));

            // accept POST request on the homepage
            app.Post("/", (req, res) => res.Send("Got a POST request"));

            // accept PUT request at /user
            app.Put("/user", (req, res) => res.Send("Got a PUT request at /user"));

            // accept DELETE request at /user
            app.Delete("/user", (req, res) => res.Send("Got a DELETE request at /user"));

            var server = app.Listen(3000);

            Console.WriteLine("Example app listening at {0}.", server.Prefixes.First());
            Console.WriteLine("Press ENTER to exit.");
            Console.ReadLine();
        }
    }
}
