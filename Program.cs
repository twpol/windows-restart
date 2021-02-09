﻿using System;
using System.Threading;

namespace Windows_Restart
{
    class Program
    {
        static void Main(bool debug)
        {
            var offsetMinute = new Random().Next(60);
            var offsetMs = offsetMinute * 60000;
            var monitor = new Monitor();
            monitor.RaiseEvent += (sender, data) => Console.WriteLine(data.Json);
            if (debug)
            {
                monitor.Execute();
            }
            else
            {
                while (true)
                {
                    Thread.Sleep(3600000 - (int)((DateTimeOffset.Now.ToUnixTimeMilliseconds() - offsetMs) % 3600000));
                    monitor.Execute();
                }
            }
        }
    }
}