using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Reports the address the device is reachable on.
///
/// This exists because the machine is headless and lives behind a scanner: when it drops
/// off the network there is otherwise nothing to look at, and finding it means sweeping
/// the LAN or pulling the SD card.
/// </summary>
public static class NetworkInfo {
    public static string DescribeAddress() {
        try {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(x));

            return address?.ToString() ?? "No network";
        }
        catch {
            return "No network";
        }
    }
}
