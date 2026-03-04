namespace Juxtens.DeviceManager.Predicates;

public static class DevicePredicates
{
    public static Func<DeviceInfo, bool> VirtualDisplay()
    {
        return device =>
        {
            if (device.HardwareIds == null)
                return false;

            foreach (var hwId in device.HardwareIds)
            {
                var upper = hwId.ToUpperInvariant();
                if (upper.Contains("ROOT\\DISPLAY") || 
                    upper.Contains("GENERICPNPMONITOR") ||
                    upper.Contains("VIRTUAL") && upper.Contains("DISPLAY"))
                {
                    return true;
                }
            }

            var friendlyName = device.FriendlyName?.ToUpperInvariant() ?? "";
            var description = device.Description?.ToUpperInvariant() ?? "";

            return (friendlyName.Contains("VIRTUAL") || description.Contains("VIRTUAL")) &&
                   (friendlyName.Contains("DISPLAY") || description.Contains("DISPLAY") || 
                    friendlyName.Contains("MONITOR") || description.Contains("MONITOR"));
        };
    }

    public static Func<DeviceInfo, bool> ByInstanceIdPrefix(string prefix)
    {
        return device => device.Id.InstanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static Func<DeviceInfo, bool> ByFriendlyName(string name)
    {
        return device => device.FriendlyName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true;
    }
}
