using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EjectDisk
{
    public abstract class BetterProcessBase
    {
        public abstract string GetFileName();

        public abstract string GetArgs();

        protected Process process;

        public virtual Process StartRun()
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = GetFileName(),
                    Arguments = GetArgs(),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                },
            };

            process.Start();
            return process;
        }

        public void Input(string input)
        {
            process.StandardInput.WriteLine(input);
            process.StandardInput.Flush();
        }

        public Task WaitForExitAsync()
        {
            return process.WaitForExitAsync();
        }

        public async Task<string[]> GetOutputAsync(params string[] inputs)
        {
            StartRun();
            foreach (var input in inputs)
            {
                Input(input);
            }
            await WaitForExitAsync();
            var result = await process.StandardOutput.ReadToEndAsync();
            return result.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public class DispartProcess : BetterProcessBase
    {
        public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync()
        {
            List<DiskInfo> results = new List<DiskInfo>();
            foreach (var line in await GetOutputAsync("list disk", "exit"))
            {
                var disk = DiskParser.GetDiskInfo(line);
                if (disk != null)
                {
                    await AddPartitions(disk);
                    results.Add(disk);

                }
            }
            return results;
        }

        private async Task AddPartitions(DiskInfo disk)
        {
            var partLines = await GetOutputAsync($"select disk {disk.ID}", "list part", "exit");
            foreach (var pLine in partLines)
            {
                var part = DiskParser.GetPartitionInfo(pLine);
                if (part != null)
                {
                    var partDetailLines = await GetOutputAsync($"select disk {disk.ID}", $"select part {part.ID}", "detail part", "exit");
                    foreach (var pdLine in partDetailLines)
                    {
                        DiskParser.ApplyPartitionDetails(part, pdLine);
                    }
                    disk.Partitions.Add(part);
                }
            }
        }

        public async Task OfflineAndOnlineAsync(string id)
        {
            var output = await GetOutputAsync($"select disk {id}", "offline disk", "online disk", "exit");
            if (!output.Any(p => p.Contains("成功")))
            {
                throw new Exception(string.Join(Environment.NewLine, output));
            }
        }

        public override string GetArgs()
        {
            return "";
        }

        public override string GetFileName()
        {
            return "diskpart";
        }
    }

    public class DiskParser
    {
        public static readonly Regex rDisk = new Regex(@"^ +磁盘 (?<ID>[\w]+) +.. +(?<Size>[\w\.]+ .B) +[\w\.]+ .?B +");
        public static readonly Regex rPartition = new Regex(@"分区 +(?<ID>[\w]+) +.* +(?<Size>[\w\.]+ .B) +[\w\.]+ .B");
        public static readonly Regex rPartitionDetail = new Regex(@"卷 +(?<Others>.+\w.+)");
        public static DiskInfo GetDiskInfo(string line)
        {
            var result = rDisk.Match(line);
            if (!result.Success)
            {
                return null;
            }
            string id = result.Groups["ID"].Value;
            string size = result.Groups["Size"].Value;
            return new DiskInfo() { ID = id, Size = size };
        }

        public static PartitionInfo GetPartitionInfo(string line)
        {
            var result = rPartition.Match(line);
            if (!result.Success)
            {
                return null;
            }
            string id = result.Groups["ID"].Value;
            string size = result.Groups["Size"].Value;
            return new PartitionInfo() { ID = id, Size = size };
        }

        public static void ApplyPartitionDetails(PartitionInfo info, string line)
        {
            var result = rPartitionDetail.Match(line);
            if (!result.Success || line.Contains("#"))
            {
                return;
            }
            info.Others = result.Groups["Others"].Value;
            new Regex("[\\s]+").Replace(info.Others, " ");
        }
    }
    public class PartitionInfo
    {
        public string ID { get; set; }
        public string Others { get; set; }
        public string Size { get; set; }

    }
    public class DiskInfo
    {
        public string ID { get; set; }
        public string Size { get; set; }
        public List<PartitionInfo> Partitions { get; } = new List<PartitionInfo>();

        public override string ToString()
        {
            return ID + "\t" + Size;
        }
    }
}