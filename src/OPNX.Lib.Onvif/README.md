# OPNX.Lib.Onvif

ONVIF SOAP client services for network video devices. The library currently supports Device service discovery, Media profiles and RTSP URI lookup, PTZ movement and presets, Imaging focus and iris control, DeviceIO relay output, and PullPoint event subscriptions.

## Basic usage

```csharp
using OPNX.Lib.Onvif;
using OPNX.Lib.Onvif.Models;

await using var client = new OnvifClient(new OnvifClientOptions
{
    DeviceServiceUri = new Uri("http://192.168.0.10/onvif/device_service"),
    UserName = "admin",
    Password = "password"
});

await client.InitializeAsync();

var profile = (await client.Media!.GetProfilesAsync()).First();
await client.Ptz!.ContinuousMoveAsync(profile.Token, 0.5f, 0, 0);
await Task.Delay(500);
await client.Ptz.StopAsync(profile.Token);
```

Always use the service addresses returned by `InitializeAsync`. Optional services are exposed as `null` when the camera does not advertise them.

## Testing without a camera

```csharp
using OPNX.Lib.Onvif.Testing;

var camera = new SimulatedOnvifCamera();
await camera.ContinuousMoveAsync("profile-1", 0.5f, 0, 0);
var token = await camera.SetPresetAsync("profile-1", "Entrance");
await camera.GotoPresetAsync("profile-1", token!);
```

The simulator validates application command flow and range conversion. It does not replace interoperability testing with physical cameras.

ONVIF is a trademark of ONVIF, Inc. This project is not affiliated with or endorsed by ONVIF.
