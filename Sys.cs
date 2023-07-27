using Akka.Actor;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using maxoms.models;

namespace maxoms
{
    internal class Sys
    {
        public static Dictionary<string, string> IConfDB { get; set; } = new Dictionary<string, string>();
        public static ILogger ILog { get; set; }
        public static ActorSystem ActSys { get; set; }
        public static string AccessID { get; set; }

        public static Sequence LastSeq { get; set; } = new Sequence();
    }
}