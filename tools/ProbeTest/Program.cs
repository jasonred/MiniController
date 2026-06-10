using MiniController.Core.Discovery;

// Usage: ProbeTest [targetIp]
var target = args.Length > 0 ? args[0] : null;
Console.WriteLine($"Probing {(target ?? "broadcast")}...");

try
{
    var devices = await MideaDiscovery.DiscoverAsync(target, TimeSpan.FromSeconds(5));
    Console.WriteLine($"Found {devices.Count} device(s).");
    foreach (var d in devices)
        Console.WriteLine($"  ip={d.Ip} port={d.Port} id={d.DeviceId} type=0x{d.DeviceType:X2} v{d.Version} name={d.Name} sn={d.SerialNumber}");
}
catch (Exception e)
{
    Console.WriteLine($"EXCEPTION: {e}");
}
