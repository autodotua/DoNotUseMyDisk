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

        public Task<string[]> GetOutputAsync(params string[] inputs)
        {
            return GetOutputAsync(TimeSpan.Zero, inputs);
        }
        public async Task<string[]> GetOutputAsync(TimeSpan timeout, params string[] inputs)
        {
            StartRun();
            foreach (var input in inputs)
            {
                Input(input);
            }
            if (timeout == TimeSpan.Zero)
            {
                await WaitForExitAsync();
            }
            else
            {
                await Task.WhenAny(((Func<Task>)(async () =>
               {
                   try
                   {
                       await WaitForExitAsync();
                   }
                   catch (Exception ex)
                   {

                   }
               }))(), Task.Delay(timeout));
                if (!process.HasExited)
                {
                    throw new TimeoutException($"进程{process.ProcessName}超时");
                }
            }
            var result = await process.StandardOutput.ReadToEndAsync();
            Debug.WriteLine(result);
            return result.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public class RemoveDriveProcess : BetterProcessBase
    {
        public RemoveDriveProcess(DiskInfo disk, bool loop)
        {
            var volumes = disk.Volumes.Where(p => !string.IsNullOrEmpty(p.LTR)).ToList();
            if (volumes.Count == 0)
            {
                throw new Exception("没有挂载点");
            }

            volumeLTR = volumes[0].LTR;
            this.loop = loop;
        }

        string volumeLTR;
        private readonly bool loop;

        public override string GetArgs()
        {
            return $"{volumeLTR}: {(loop ? "-L" : "")}";
        }

        public override string GetFileName()
        {
            if (IntPtr.Size == 4)
            {
                return "RemoveDrive_32.exe";
            }
            return "RemoveDrive_64.exe";
        }

    }

    public class DispartProcess : BetterProcessBase
    {
        public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync()
        {
            List<DiskInfo> disks = new List<DiskInfo>();
            foreach (var line in await GetOutputAsync("list disk", "exit"))
            {
                var disk = DiskParser.GetDiskInfo(line);
                if (disk != null)
                {
                    await foreach (var volume in GetVolumes(disk))
                    {
                        disk.Volumes.Add(volume);
                    }
                    disks.Add(disk);
                }
            }
            return disks;
        }

        private async IAsyncEnumerable<VolumeInfo> GetVolumes(DiskInfo disk)
        {
            var volumeLines = await GetOutputAsync($"select disk {disk.ID}", "detail disk", "exit");
            foreach (var line in volumeLines)
            {
                var volume = DiskParser.GetVolumeInfo(line);
                if (volume != null)
                {
                    yield return volume;
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

        public async Task DismountAsync(DiskInfo disk)
        {
            foreach (var volume in disk.Volumes)
            {
                var output = await GetOutputAsync($"select volume {volume.ID}", "remove all dismount", "exit");
                if (!output.Any(p => p == "DiskPart 成功地删除了驱动器号或装载点。")
                    || !output.Any(p => p == "DiskPart 成功地卸载了此卷并使其脱机。"))
                {
                    throw new Exception(string.Join(Environment.NewLine, output));
                }
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
        public static readonly Regex rDisk = new Regex(@"磁盘 (?<ID>[\w]+) +.. +(?<Size>[\w\.]+ .B) +[\w\.]+ .?B +");
        public static readonly Regex rPartition = new Regex(@"分区 +(?<ID>[\w]+) +.* +(?<Size>[\w\.]+ .B) +[\w\.]+ .B");
        public static readonly Regex rVolume = new Regex(@"卷 (?<ID>\w+) ((?<LTR>[A-Z]) )?((?<Label>.+) )?(?<FS>[A-Z\w]+) (磁盘分区|可移动) (?<Size>\w+ .?B) ");
        public static readonly Regex rVolumeDetail = new Regex(@"卷 +(?<Others>.+\w.+)");

        public static DiskInfo GetDiskInfo(string line)
        {
            var result = rDisk.Match(line);
            if (!result.Success)
            {
                return null;
            }
            return new DiskInfo()
            {
                ID = result.Groups["ID"].Value,
                Size = result.Groups["Size"].Value
            };
        }

        public static VolumeInfo GetVolumeInfo(string line)
        {
            var result = rVolume.Match(ToSingleSpace(line));
            if (!result.Success)
            {
                return null;
            }
            return new VolumeInfo()
            {
                ID = result.Groups["ID"].Value,
                FileSystem = result.Groups["FS"].Value,
                LTR = result.Groups["LTR"].Value,
                Label = result.Groups["Label"].Value,
                Size = result.Groups["Size"].Value,
            };
        }

        private static readonly Regex rSpace = new Regex("[\\s]+");

        private static string ToSingleSpace(string line)
        {
            return rSpace.Replace(line.Trim(), " ");
        }
    }

    public class VolumeInfo
    {
        public string ID { get; set; }
        public string LTR { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public string Size { get; set; }
    }
    public class DiskInfo
    {
        public string ID { get; set; }
        public string Size { get; set; }
        public List<VolumeInfo> Volumes { get; } = new List<VolumeInfo>();

    }
}