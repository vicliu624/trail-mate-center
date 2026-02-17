# RJCP.DeviceMgr <!-- omit in toc -->

This library implements mechanisms to enumerate over the devices that are
present in your Windows System. It uses Win32 API from `CfgMgr32.dll` which is
available since Windows 2000. This code is highly compatible and is tested with
Windows XP to Windows 11.

The current implementation provides *readonly* information. It is not designed
that you can eject or modify the devices in the system.

It is called `RJCP.IO.DeviceMgr`, as it provides information very similar to
when you manage your devices from the Windows Desktop and view the details of
individual devices.

## Testing

To quickly test the usage of the library, run the executable `DeviceInfoDump`.
This gets the root tree node and dumps all information about the device tree to
the console. This gives you an idea of the information that can be obtained.

## Using in Your Own Software

Import the library into your project.

To set up logging, to see any errors or warnings, see
[Logging.md](docs/Logging.md), and the example program `DeviceInfoDump` which is
a minimal program for dumping information.
