using System;
using FFXIVPingMachina.PingMonitor;
using Machina.FFXIV;

namespace FFXIVPingMachina
{
    class Program
    {
        static void Main(string[] args)
        {
            var pm = new PacketMonitor();
            pm.OnPingSample += PmOnOnPingSample;

            var monitor = new FFXIVNetworkMonitor();
            monitor.MessageReceived = pm.MessageReceived;
            monitor.MessageSent = pm.MessageSent;
            monitor.Start();


            Console.WriteLine("Press any key to stop...");
            Console.ReadKey();

            monitor.Stop();
        }

        private static void PmOnOnPingSample(double rtt, DateTime sampleTime)
        {
            Console.Out.WriteLine($"RTT={rtt}.");
        }
    }

}
