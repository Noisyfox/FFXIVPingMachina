﻿using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using LibPingMachina.PingMonitor;
using LibPingMachina.PingMonitor.handler;
using Machina.Infrastructure;

namespace FFXIVPingMachina.PingMonitor
{
    public delegate void ConnectionPingSampleDelegate(ConnectionPing ping);

    public class PacketMonitor
    {
        public event IPCPingOpCodeDetector.PingOpCodeDetectDelegate OnPingOpCodeDetected;
        public event ConnectionPingSampleDelegate OnPingSample;
        public ConnectionPing CurrentPing { get; private set; }

        private readonly Dictionary<string, PerConnectionMonitor> _connections =
            new Dictionary<string, PerConnectionMonitor>();

        public void MessageSent(TCPConnection connection, long epoch, byte[] message)
        {
            var id = ConnectionIdentifier.GetStringIdentifier(connection);
            if (!_connections.TryGetValue(id, out var monitor))
            {
                monitor = CreatePerConnectionMonitor(id);
                _connections[id] = monitor;
            }

            try
            {
                monitor.MessageSent(epoch, message);
            }
            catch (ParseException ex)
            {
                Console.Out.WriteLine(ex.ToString());
            }
            finally
            {
                CheckActivity();
            }
        }

        public void MessageReceived(TCPConnection connection, long epoch, byte[] message)
        {
            var id = ConnectionIdentifier.GetStringIdentifier(connection);
            if (!_connections.TryGetValue(id, out var monitor))
            {
                monitor = CreatePerConnectionMonitor(id);
                _connections[id] = monitor;
            }

            try
            {
                monitor.MessageReceived(epoch, message);
            }
            catch (ParseException ex)
            {
                Console.Out.WriteLine(ex.ToString());
            }
            finally
            {
                CheckActivity();
            }
        }

        private PerConnectionMonitor CreatePerConnectionMonitor(string connection)
        {
            var monitor = new PerConnectionMonitor(connection);
            monitor.OnPingSample += MonitorOnOnPingSample;
            monitor.OnPingOpCodeDetected += code => OnPingOpCodeDetected?.Invoke(code);

            return monitor;
        }

        private void CheckActivity()
        {
            var now = DateTime.UtcNow;
            _connections.Where(it => now.Subtract(it.Value.LastActivity).TotalMinutes > 2)
                .Select(it => it.Key).ToList().ForEach(k => _connections.Remove(k));
        }

        private void MonitorOnOnPingSample(ConnectionPing ping)
        {
            CurrentPing = _connections.Select(it => it.Value.CurrentPing).Max();
            OnPingSample?.Invoke(CurrentPing);
        }
    }
}
