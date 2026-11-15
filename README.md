# Boot Camp Eject and Fn shortcuts

Turn the built-in Eject key (⏏) into forward Delete and use the bottom-left Fn key for selected Ctrl shortcuts on a 2011 MacBook Pro running Windows.

## Compatibility

The remapping logic was physically tested on a **13-inch 2011 MacBook Pro (MacBookPro8,1)** with its internal USB keyboard **VID 05AC, PID 0253, interface 0**, Windows 10 IoT Enterprise LTSC x64 (build 19044), Boot Camp 5.1.5621, Apple KeyMagic 5.0.8.0, and USBPcap 1.5.4.0. The standalone installer packages that implementation. Other models and OS builds are unverified.

Fn continues to work with the top row through Boot Camp. Fn alone does not act as Ctrl. The original Ctrl key continues to work normally.

## Shortcuts

| Physical keys | Windows input and typical action |
| --- | --- |
| ⏏ | Delete, including hold-to-repeat |
| Fn + left Shift | Win + Space, cycle keyboard layouts on release |
| Fn + A / X / C / V | Ctrl + A / X / C / V, select all / cut / copy / paste |
| Fn + Z / Y | Ctrl + Z / Y, undo / redo where supported |
| Fn + S / O / N | Ctrl + S / O / N, save / open / new document or window |
| Fn + F | Ctrl + F, find |
| Fn + L / R | Ctrl + L / R, browser address bar / reload |
| Fn + T / W | Ctrl + T / W, open / close a tab |
| Fn + Shift + T | Ctrl + Shift + T, reopen a closed tab |
| Fn + Tab / Fn + Shift + Tab | Ctrl + Tab / Ctrl + Shift + Tab, next / previous tab |
| Fn + Shift + V | Ctrl + Shift + V, paste as plain text where supported |

Actions follow the active application's handling of these shortcuts. Fn + left Shift switches layouts only when used without another key in the same gesture.

## Install

1. Install [64-bit PowerShell 7 via Microsoft's MSI](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows) to its default location. Use PowerShell 7.4 or later.
2. Install [USBPcap 1.5.4.0](https://github.com/desowin/usbpcap/releases/tag/1.5.4.0) to its default Program Files location, then reboot. USBPcap includes a USB capture driver. The remapper does not change the Apple driver or require Windows test-signing mode.
3. Open PowerShell 7 as administrator from the Windows account that will use the remap. From the repository directory, review the source and run:

   ```powershell
   .\install.ps1
   ```

The installer copies the scripts under `%ProgramFiles%\BootCampEjectFnShortcuts`, registers an elevated interactive task named `BootCampEjectFnShortcuts` for that account, and starts it. Keeping the scripts under Program Files limits modification by standard users on a default Windows installation. The task starts again at sign-in. Install only one instance of this remapper on the same Windows installation.

For a quick check, type `ABCDE` in Notepad, put the cursor after B, and press ⏏. Expect `ABDE`. Then try copying a known selection with Fn+C and cycling the layout with Fn+left Shift.

## Stop or uninstall

- To stop until the next sign-in, run the installed `stop.ps1` in the same desktop session using elevated PowerShell.
- To remove the startup task, run `.\install.ps1 -Uninstall` in elevated PowerShell. The installer checks the task's ownership before removing it. Afterward you can remove the installed directory if you no longer need the source or logs. USBPcap is a separate dependency and may be uninstalled separately.
- If no shortcuts work, inspect `%ProgramFiles%\BootCampEjectFnShortcuts\logs\service.log`. It contains startup and error messages.

## How it works and what to port

USBPcap is connected to the built-in Apple keyboard's current USB address and streams reports to the worker through a pipe. The parser checks the USB device and configuration descriptors before using input reports. The confirmed format is a 10-byte report with ID 1, with Eject and Fn in bits 0 and 1 of the last byte. The worker reads keyboard reports in memory. It creates no PCAP files and saves no keystrokes to disk.

- `src/EjectPcapReader.cs` validates the USB source and decodes the report.
- `src/EjectEngine.cs` sends forward Delete and handles hold-to-repeat.
- `src/FnShortcuts.cs` combines observed Fn state with a Windows keyboard hook. It ignores injected input and replaces only configured key gestures.
- `src/eject-service.ps1` discovers the USB device, manages the worker, and reconnects on errors.

For another model, first verify the USB device ID, interface, report ID, length, and Fn/Eject masks. Update the device checks in the reader, worker, and installer together. The USB interface's display name is used to locate the capture candidate and may vary with the installed driver.

Very fast simultaneous presses can reach the Windows hook before Fn is visible in the USB stream. Press Fn first if a shortcut is missed. The hook cannot distinguish an external keyboard's event from the built-in keyboard's event, so a mapped key on an external keyboard can use the shortcut while the built-in Fn is held. Delete repeat is approximate. The task runs in the signed-in desktop session. The lock screen, UAC secure desktop, and before-sign-in use are outside scope.

USBPcap and PowerShell are installed separately. This repository contains only the remapper and its installer. The remapper is licensed under MIT.
