# AutomaticPaperlessUploader

Service that presents a Raspberry Pi as a USB flash drive to a scanner, then uploads
whatever is scanned to a [Paperless-ngx](https://docs.paperless-ngx.com/) server.

Intended to run on a Raspberry Pi Zero 2 W in USB gadget mode.

## How it works

The Pi exposes a disk image over USB using the `g_mass_storage` gadget, so the scanner
sees an ordinary flash drive. Two images alternate:

- one is **exposed** to the scanner
- one is **spare**, already emptied

Pressing any key on the keypad swaps them. The gadget's LUN backing file is writable in
sysfs, so the swap happens in place and the USB device is never disconnected. The scanner
gets a clean drive back in about a second, while the just released image is loop mounted
on the Pi and its files are uploaded to Paperless. Uploaded files are deleted, which
leaves that image clean and ready to become the spare for the next cycle.

Document metadata such as correspondent and document type is not chosen at scan time.
Paperless handles classification after upload.

## Configuration

`appsettings.json` holds non secret settings:

| Section | Purpose |
| --- | --- |
| `UserInput.AnyKeySubmits` | When true, any key starts an upload and `Actions` is ignored |
| `UserInput.KeyMatrix` | Keypad layout and GPIO pins |
| `Storage.Images` | The two images that alternate |
| `Storage.LunFilePath` | Sysfs file that controls the exposed media |
| `Storage.SubmitCooldownMs` | Debounce window for repeat key presses |
| `Storage.QuietPeriodMs` | How long the drive must go untouched before the scanner counts as finished |
| `Storage.MaxWaitForQuietMs` | How long to wait for that before swapping anyway |
| `Paperless.BaseUrl` | Paperless server |
| `Paperless.Tags` | Tag names applied at upload, resolved to ids at runtime |
| `Paperless.AllowedExtensions` | File types that get uploaded |

### Tags as workflow triggers

`Paperless.Tags` is currently `Change Ownership to Elise`. That tag exists to fire a
Paperless workflow, which reassigns document ownership and then **removes the tag**.

This is expected. Do not treat a missing tag as a failed upload, and do not verify uploads
by searching for documents carrying the tag, because a correctly processed document will
not have it. Check that the document arrived instead.

Tags are looked up by name through `/api/tags/` on first use rather than hardcoded as ids,
so renaming or recreating the tag in Paperless surfaces a clear error instead of silently
tagging the wrong thing. This is why the API token needs tag read permission in addition
to upload permission.

The API token is **not** stored here. Supply it through the environment file referenced by
the systemd unit:

```sh
sudo install -m 600 /dev/null /etc/automaticpaperlessuploader.env
echo 'PAPERLESS__TOKEN=your-token-here' | sudo tee /etc/automaticpaperlessuploader.env
```

Generate the token in Paperless under **Settings → My Profile → API Auth Token**.

## Setting up the device

See [SETUP.md](SETUP.md) for a full rebuild from a blank SD card: gadget mode, the backing
images, keypad wiring, the .NET runtime, the API token and deployment.

The short version:

```
dtoverlay=dwc2,dr_mode=peripheral   # /boot/firmware/config.txt
dwc2                                 # /etc/modules
@reboot /sbin/modprobe g_mass_storage file=/piusb.bin stall=0 removable=y   # root crontab
```

`removable=y` matters. It tells the host the media can change, which is what makes the
in place swap work.

Connect the scanner to the Pi's **inner** USB port. The outer one is power only.

## Deploying

```sh
dotnet publish -c Release -o /srv/AutomaticPaperlessUploader
sudo cp AutomaticPaperlessUploader.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now AutomaticPaperlessUploader
journalctl -u AutomaticPaperlessUploader -f
```

## Failure handling

Pressing the key while a scan is still being written would hand the upload a half
finished file, so the swap waits for the drive to go untouched for `QuietPeriodMs`
first. There is no kernel signal for this: the mass storage LUN exposes only its
backing file, read only flag and eject controls, so the mtime of that backing file is
used as a proxy, since the gadget writes the host's data through it.

If the scanner never goes quiet within `MaxWaitForQuietMs` the swap happens anyway and
logs a warning. A key press that appears to do nothing is worse than the risk, but the
risk is real: a document caught mid write may be incomplete.


A file that fails to upload is left on the image rather than deleted, so the next cycle
retries it. Only one upload cycle runs at a time; keys pressed during a cycle are ignored.

## Status display (optional)

An SSD1306 OLED over I2C shows what the device is doing. It is entirely optional: if the
bus is disabled, the module is unplugged, or a write fails, the indicator disables itself
and uploading carries on. Feedback hardware must never take down the thing it reports on.

### Wiring

| Display | Pi |
| --- | --- |
| VCC | 3V3 (pin 1) |
| GND | GND (pin 6) |
| SDA | GPIO2 (pin 3) |
| SCL | GPIO3 (pin 5) |

The keypad already uses GPIO 5, 6, 12, 13, 16, 19, 20 and 21, so the I2C pins are clear.

### Panel size

Set `Display.Height` to match the panel. 0.96 inch modules are usually 128x64 and 0.91
inch modules are usually 128x32, and both answer on the same address, so the scan cannot
tell them apart.

Getting it wrong is not obvious: a 32 pixel panel driven as 64 still shows the top half,
so the headline appears and anything below y=32 silently vanishes.

A short panel only has room for two lines, so it shows the headline plus **either** the
detail or the address: the detail when something is happening or has failed, the address
when idle. On a 64 pixel panel all three fit.

`dotnet run -- --preview-screens` renders every state at both sizes.

### Enabling

Uncomment in `/boot/firmware/config.txt`, then reboot:

```
dtparam=i2c_arm=on
```

Confirm the panel is seen, usually at `0x3C`:

```sh
i2cdetect -y 1
```

Then set `Display.Enabled` to `true` in `appsettings.json`. `Address` is decimal there, so
`0x3C` is `60`.

### If /dev/i2c-1 is missing after a reboot

`dtparam=i2c_arm=on` loads the controller driver, but the character device only appears
once the `i2c-dev` module is loaded as well:

```sh
sudo modprobe i2c-dev
echo i2c-dev | sudo tee -a /etc/modules   # persist across reboots
```
### What it shows

Headline status, a detail line, and the device's IP address. The address matters more than
it looks: this machine is headless and lives behind a scanner, so when it drops off the
network there is otherwise nothing to look at.

Failures stay on screen until the next cycle. Everything else blanks after
`BlankAfterSeconds` to avoid burning the panel, since a status display would otherwise
show "Ready" for weeks.

### Previewing without hardware

Renders every screen to PNG so layout can be reviewed before the panel arrives:

```sh
dotnet run -- --preview-screens
```

### Notes

`Iot.Device.Bindings.SkiaSharpAdapter` provides text rendering. `System.Drawing.Common` is
Windows only from .NET 6 onward, so it is not an option here. SkiaSharp works on
`linux-arm64` without fontconfig installed, and silently falls back when a font family is
missing, so `FontFamily` is a preference rather than a guarantee.
