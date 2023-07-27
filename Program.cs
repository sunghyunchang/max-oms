using Serilog.Events;
using Serilog;
using Akka.Actor;
using maxoms.comm;
using maxoms.assist;

namespace maxoms
{
    class Program
    {
        #region Main
        static void Main(string[] args) 
        {
            Sys.ILog = new LoggerConfiguration()
             .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
             .Enrich.FromLogContext()
             .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fffff}] [{Level:u3}] {Message:lj}{NewLine}")
             .WriteTo.File(
                  "logs/log_.txt",
                 rollingInterval: RollingInterval.Day,
                 outputTemplate: "[{Timestamp:HH:mm:ss.fffff}] [{Level:u3}] {Message:lj}{NewLine}",
                 fileSizeLimitBytes: 50_000_000,
                 rollOnFileSizeLimit: true,
                 shared: true,
                 flushToDiskInterval: TimeSpan.FromSeconds(1),
                 retainedFileCountLimit: 500)
             .CreateLogger();

            if (args.Length != 2)
            {
                Sys.ILog.Error("check argument");
                Environment.Exit(2);
            }

            Sys.ILog.Information($"AccessID='{args[0]}' DB='{args[1]}'");

            Sys.AccessID = args[0];            
            DB.GetTbServiceConfig(args[1]);


            Sys.ActSys = ActorSystem.Create("ActSys");

            Sys.ActSys.ActorOf(Props.Create<OmsTcp1>(), "1"); // OMS-1 Port Actor : JOB Regis/Cancle Request/Ack
            Sys.ActSys.ActorOf(Props.Create<OmsTcp2>(), "2"); // OMS-2 Port Actor : New/Cancel Order Request
            Sys.ActSys.ActorOf(Props.Create<OmsTcp3>(), "3"); // OMS-3 Port Actor : New/Cancel Order Ack & Execution

            Sys.ActSys.WhenTerminated.Wait();
        }
        #endregion
    }
}