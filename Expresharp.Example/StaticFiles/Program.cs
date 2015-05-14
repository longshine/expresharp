using System;
using System.Linq;
using Expresharp;

namespace StaticFiles
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new Express();

            app.Use(Express.Static("public"));

            app.Use("/static", Express.Static("public"));

            var server = app.Listen(3000);

            Console.WriteLine("Example app listening at {0}.", server.Prefixes.First());
            Console.WriteLine("Press ENTER to exit.");
            Console.ReadLine();
        }
    }
}
