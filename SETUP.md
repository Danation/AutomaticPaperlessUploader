# Building the device from scratch

Everything needed to rebuild this on a blank SD card. Written against the machine that is
actually running, so the commands match reality rather than a plan.

## Hardware

| | |
| --- | --- |
| Board | Raspberry Pi Zero 2 W |
| OS | Debian 12 (bookworm), 64 bit |
| Input | 4x4 matrix membrane keypad |
| Display | SSD1306 128x64 OLED, I2C (optional) |
| Scanner | Brother, connected by USB |

The Zero 2 W matters in two ways: it is 2.4GHz only, so it cannot join a 5GHz network, and
its sysfs paths differ from other models (see [Finding the LUN path](#finding-the-lun-path)).

## 1. USB gadget mode

The Pi has to present itself as a USB device rather than a host.

Add to the bottom of `/boot/firmware/config.txt`:

```
dtoverlay=dwc2,dr_mode=peripheral
```

Add the module so it loads at boot:

```sh
echo dwc2 | sudo tee -a /etc/modules
```

Reboot, then confirm:

```sh
lsmod | grep dwc2
```

**Use the inner USB port** for the scanner. The outer one is power only. This is the single
most common way to waste an hour on this project.

## 2. Backing images

Two images alternate: one is exposed to the scanner, the other is mounted locally while its
contents upload.

```sh
sudo dd bs=1M if=/dev/zero of=/piusb.bin count=4096
sudo mkdosfs /piusb.bin -F 32 -I
sudo cp /piusb.bin /piusb2.bin
sudo mkdir -p /mnt/usb_share
```

4GB each. FAT32 because that is what scanners expect.

## 3. Load the gadget at boot

Via root's crontab, which is what the running machine uses. `/etc/rc.local` does not exist
on bookworm.

```sh
sudo crontab -e
```

Add:

```
@reboot /sbin/modprobe g_mass_storage file=/piusb.bin stall=0 removable=y
```

`removable=y` is what makes the whole design work. It tells the host the media can change,
so swapping the backing file mid-session reads as a disc swap rather than a fault.

After a reboot the exposed image is always `/piusb.bin`, whichever was exposed before. Both
images are normally empty, so this is harmless.

## Finding the LUN path

`Storage.LunFilePath` in `appsettings.json` is board specific. On the Zero 2 W:

```
/sys/devices/platform/soc/3f980000.usb/gadget.0/lun0/file
```

On a different model, find it with:

```sh
sudo find /sys/devices -path '*gadget*/lun0/file'
```

Writing a path to that file swaps the exposed media in place, and writing an empty string
ejects it. That is the mechanism behind the whole swap, and the reason the service does not
need to unload and reload the kernel module.

## 4. Keypad

Any key press starts an upload. The layout in `appsettings.json` only matters for logging,
since every key does the same thing.

| Role | BCM | Header pin |
| --- | --- | --- |
| Row 0 | GPIO5 | 29 |
| Row 1 | GPIO6 | 31 |
| Row 2 | GPIO13 | 33 |
| Row 3 | GPIO19 | 35 |
| Col 0 | GPIO12 | 32 |
| Col 1 | GPIO16 | 36 |
| Col 2 | GPIO20 | 38 |
| Col 3 | GPIO21 | 40 |

Rows are `OutputPins`, columns are `InputPins`, and the pins are read with
`PinMode.InputPullDown`. Which ribbon lead is row 0 depends on the keypad, so if presses
register as the wrong key, swap the two groups before changing anything in code.

Note this occupies three of the four hardware PWM pins, leaving only GPIO18. That rules out
smooth PWM dimming on a status LED, though plain digital colours still work.

## 5. .NET runtime

The service is a .NET 10 worker. Install the arm64 SDK under `/opt/dotnet` and symlink it
so systemd and a login shell agree on the path:

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet
sudo ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
dotnet --version
```

The install script places versions side by side, so an older runtime already there is left
alone and can still be rolled back to.

The unit sets `DOTNET_ROOT=/opt/dotnet` because it is not on root's path by default.

## 6. Paperless token

Create the token in Paperless under **Settings, My Profile, API Auth Token**. It needs
permission to upload documents and to read tags: tags are resolved by name at runtime rather
than hardcoded as ids.

```sh
sudo install -o root -g root -m 600 /dev/null /etc/automaticpaperlessuploader.env
echo 'PAPERLESS__TOKEN=your-token-here' | sudo tee /etc/automaticpaperlessuploader.env
```

Root only, mode 600, and never in the repository. The unit reads it with `EnvironmentFile`.

## 7. Deploy

```sh
git clone git@github.com:Danation/AutomaticPaperlessUploader.git
cd AutomaticPaperlessUploader
export DOTNET_ROOT=/opt/dotnet
dotnet publish -c Release -o /tmp/apu-pub
sudo mkdir -p /srv/AutomaticPaperlessUploader
sudo cp -r /tmp/apu-pub/. /srv/AutomaticPaperlessUploader/
sudo cp AutomaticPaperlessUploader.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now AutomaticPaperlessUploader
journalctl -u AutomaticPaperlessUploader -f
```

The service runs as **root** because it writes the gadget LUN in sysfs and loop mounts the
images. That is a deliberate choice for a single purpose appliance rather than an oversight.

## 8. Display (optional)

See [the display section of the README](README.md#status-display-optional).

## Verifying

```sh
systemctl is-active AutomaticPaperlessUploader     # active
cat /sys/class/udc/*/state                          # configured, once the scanner is attached
cat /sys/devices/platform/soc/3f980000.usb/gadget.0/lun0/file   # /piusb.bin or /piusb2.bin
hostname -I                                         # on the network
```

Then scan something and press a key. The log should show a swap, a mount, an upload and an
unmount inside a couple of seconds.

## Troubleshooting

**Scanner does not see a drive.** Check the inner USB port, then `cat /sys/class/udc/*/state`.
`not attached` means no host is talking to it.

**Device is not on the network.** The Zero 2 W is 2.4GHz only and will not see a 5GHz-only
SSID. Wifi is stored in `/etc/NetworkManager/system-connections/`. If it is unreachable and
headless, the SD card can be edited from another machine: the rootfs is ext4, so a Linux box
or WSL with `usbipd` will do. This is the argument for the optional display, which shows the
address on every screen.

**Presses do nothing.** Look for `Key Event Raised` in the log. Nothing there means wiring or
pin mapping; see [Keypad](#4-keypad).

**Uploads fail.** Files are left on the image deliberately, so nothing is lost and the next
press retries. Check the token and that Paperless is reachable.

**Dates are wrong in Paperless.** `08/04/2026` arriving as 8 April is Paperless parsing day
first. Set `PAPERLESS_DATE_ORDER=MDY` on the server; it is not something this service
controls.
