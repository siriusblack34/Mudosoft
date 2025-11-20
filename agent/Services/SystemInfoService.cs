// agent/Services/SystemInfoService.cs
using System;
using System.IO;

// Eklendi: Arayüzlerin (Interfaces) bulunduğu namespace
using Mudosoft.Agent.Interfaces; 

namespace Mudosoft.Agent.Services
{
    // Düzeltildi: Arayüz implementasyonu eklendi
    public class SystemInfoService : ISystemInfoService
    {
        private readonly Random _rnd = new();

        public double GetCpuUsage() => _rnd.Next(1, 40);

        public double GetRamUsage() => _rnd.Next(20, 80);

        public double GetDiskUsage()
        {
            try
            {
                var drive = DriveInfo.GetDrives()[0];
                double used = drive.TotalSize - drive.AvailableFreeSpace;
                double percent = (used / drive.TotalSize) * 100.0;
                return Math.Round(percent, 1);
            }
            catch
            {
                return _rnd.Next(10, 70);
            }
        }
    }
}