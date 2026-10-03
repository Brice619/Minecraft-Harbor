using System.Runtime.InteropServices;
namespace MinecraftHarbor;
internal static class SystemMemory
{
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKilobytes);
    internal static int InstalledGB {get;}=Read();
    static int Read(){if(!GetPhysicallyInstalledSystemMemory(out var kb)||kb==0)throw new InvalidOperationException("Windows could not report installed memory.");return checked((int)(kb/(1024*1024)));}
    internal static void Validate(int gb){if(gb<8||gb>InstalledGB)throw new ArgumentException($"Choose between 8 and {InstalledGB} GB of installed RAM.");}
    internal static int[] Choices(int maximum,int step)=>Enumerable.Range(1,Math.Max(0,maximum/step)).Select(i=>i*step).ToArray();
}
