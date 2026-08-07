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

## One time Pi setup

Enable peripheral mode in `/boot/firmware/config.txt`:

```
dtoverlay=dwc2,dr_mode=peripheral
```

Create the two images:

```sh
sudo dd bs=1M if=/dev/zero of=/piusb.bin count=4096
sudo mkdosfs /piusb.bin -F 32 -I
sudo cp /piusb.bin /piusb2.bin
```

Load the gadget at boot by adding this to `/etc/rc.local` or a systemd unit:

```sh
/sbin/modprobe g_mass_storage file=/piusb.bin stall=0 removable=y
```

`removable=y` matters. It tells the host the media can change, which is what makes the
in place swap work.

Connect the scanner to the Pi's **data** USB port, not the power only port.

## Deploying

```sh
dotnet publish -c Release -o /srv/AutomaticPaperlessUploader
sudo cp AutomaticPaperlessUploader.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now AutomaticPaperlessUploader
journalctl -u AutomaticPaperlessUploader -f
```

## Failure handling

A file that fails to upload is left on the image rather than deleted, so the next cycle
retries it. Only one upload cycle runs at a time; keys pressed during a cycle are ignored.
