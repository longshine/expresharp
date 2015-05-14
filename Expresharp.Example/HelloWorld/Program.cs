using System;
using System.Linq;
using Expresharp;

namespace HelloWorld
{
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
}
