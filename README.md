# Ink Pad Resetter

A small Windows app that clears the "Service required" (waste ink pad full) lock on an
Epson L120 by resetting its waste ink counters over USB.

Resetting the counters doesn't empty the pad. Clean or replace it, or fit an external
waste tank, or ink can leak out of the printer.

## Use

1. Connect the L120 by USB, turn it on, and make sure no print job is stuck in the queue.
2. Run `dist\InkPadResetter.exe`. It reads the printer and backs up the whole EEPROM
   (512 bytes) to `Documents\Ink Pad Resetter\Backups` before the Reset button unlocks.
3. Tick the pad checkbox, press **Reset counters**, then turn the printer off and on.

Each byte is read back after it's written. If any check fails, the bytes changed so far
get their old values back.

## How it works

- `UsbPrintPort` opens the printer through `usbprint.sys`, the driver Windows already
  uses, so no driver swap is needed.
- `D4Link` speaks IEEE 1284.4 (D4) transaction revision 0x10 (the L120 refuses 0x20)
  to open the `EPSON-CTRL` channel.
- `EpsonSession` sends `di` (device ID), `st` (status) and the `||` factory commands
  `A` (read EEPROM) and `B` (write EEPROM).
- The L120's read key `0x0392`, write key and counter addresses come from
  [reinkpy](https://codeberg.org/ircama/reinkpy)'s model table and were checked on a
  real L120. No reinkpy code is used.

| Address | Meaning | Reset to |
|---|---|---|
| 0x18–0x19 | main waste counter (little-endian) | 00 00 |
| 0x1C–0x1D | second counter | 00 00 |
| 0x1E | waste counter | 00 |
| 0x2E | level byte | 5E |

## Build

```bash
dotnet test tests/InkPadResetter.Tests
dotnet publish src/InkPadResetter.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

The tests run against `FakePrinter`, a byte-level L120 simulator.

Headless helpers (output goes to a file because the app has no console):

- `InkPadResetter.exe --read report.txt` reads status and counters and never writes.
- `InkPadResetter.exe --render <folder>` saves a PNG of each window state.
