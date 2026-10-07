# mavlinkx CLI

`mavlinkx` is a command line tool for MAVLink diagnostics: record live traffic, find out what is in a capture, replay a capture, and inspect dialect definitions without writing any code.

## Installation

```bash
dotnet tool install --global MavLinkSharp.Cli
```

Verify the installation:

```bash
mavlinkx info
```

The tool targets .NET 10, so a .NET 10 runtime must be installed. Update it with `dotnet tool update --global MavLinkSharp.Cli`, or remove it with `dotnet tool uninstall --global MavLinkSharp.Cli`.

## Dialects

Every command accepts `--dialect`. The value is either the name of a bundled dialect, such as `common` or `ardupilotmega`, or a path to a dialect XML file:

```bash
mavlinkx info --dialect ardupilotmega
mavlinkx schema message ATTITUDE --dialect ./my-vehicle.xml
```

List every bundled dialect:

```bash
mavlinkx info --json
```

## Endpoints

`capture` and `replay` take endpoint URIs.

| Endpoint | Meaning |
| --- | --- |
| `udp://:14550` | Listen for MAVLink on UDP port 14550 |
| `udp://:14550 --remote 192.168.1.5:14550` | Listen and answer a fixed peer |
| `tcp://:5760` | Accept one TCP connection on port 5760 |
| `tcp://192.168.1.5:5760` | Connect out to a TCP endpoint |
| `serial://COM3:57600` | Open a serial port at the given baud rate |

## capture

Records live traffic into a trace file. The format follows the output extension: `.raw` stores packet bytes, anything else stores JSON lines.

```bash
mavlinkx capture udp://:14550 -o session.raw
mavlinkx capture serial://COM3:57600 -o session.jsonl --include-raw
```

Useful options:

| Option | Effect |
| --- | --- |
| `--include-raw` | Embed packet bytes in a JSON lines trace so `replay` stays byte exact |
| `--duration <seconds>` | Stop automatically after a fixed time |
| `--max-packets <count>` | Stop automatically after a fixed number of packets |
| `-f, --filter <expr>` | Only capture matching messages |
| `--secret-key <key>` | Validate incoming MAVLink 2 signatures |
| `-v` | Echo a one line summary per packet to stderr |

`--duration` is the reliable way to stop a capture, because a serial port read cannot be interrupted reliably.

### Filters

A filter term is a message id (`30`), an inclusive range (`40-50`), or a message name, optionally with wildcards (`ATTITUDE`, `MSG_*`, `ATT?TUDE`). Terms are repeatable and additive, and a term prefixed with `!` (or `exclude:`) removes matches instead:

```bash
mavlinkx capture udp://:14550 -o all.raw -f '40-50'
mavlinkx capture udp://:14550 -o interesting.raw -f 'ATTITUDE' -f 'GLOBAL_POSITION*' -f '!ATTITUDE_TARGET'
```

## inspect

Decodes a recorded trace offline. This is the fastest way to find out what a vehicle is actually sending.

```bash
mavlinkx inspect session.raw
mavlinkx inspect session.raw -v --limit 5
mavlinkx inspect session.raw --json > summary.json
```

Without `-v` each packet gets one summary line followed by a count per message. With `-v` every field is printed with its value, type, and unit where the dialect defines one.

A `.raw` trace is scanned as a raw byte stream, so a capture with a damaged frame reports where the damage is instead of stopping:

```console
17:33:57.534 V2 HEARTBEAT     id=0  sys=1 comp=1 seq=0 len=9
17:33:57.550 V2 HEARTBEAT     id=0  sys=1 comp=1 seq=1 len=9
mavlinkx: offset 21: MessageNotFound (40 bytes skipped)
17:33:57.550 V2 ATTITUDE      id=30 sys=1 comp=1 seq=11 len=28

9 packets shown, 1 undecodable regions
  HEARTBEAT      5
  ATTITUDE       4
```

## replay

Resends a trace to a target. Without `--target` the trace is only decoded, which is a quick way to validate a capture.

```bash
mavlinkx replay session.raw --dry-run
mavlinkx replay session.raw --target udp://127.0.0.1:14550
mavlinkx replay session.raw --target serial://COM3:57600 --timing --speed 2 --loop
```

Packets are sent byte for byte when the trace carries raw bytes, which is the case for `.raw` traces and for JSON lines traces captured with `--include-raw`. A JSON lines trace captured without raw bytes is re-encoded from its decoded fields, which is only byte exact if the dialect has no fields the trace dropped.

## schema

Inspects dialect definitions.

```bash
mavlinkx schema messages
mavlinkx schema message ATTITUDE
mavlinkx schema enums -f 'MAV_TYPE'
mavlinkx schema enum MAV_TYPE
```

## info

Reports the tool and runtime versions and what the selected dialect contains:

```console
mavlinkx         1.14.0
MavLinkSharp     1.14.0
dialect          common
runtime          .NET 10.0.12
architecture     X64
messages         221
enums            136
```

## Trace formats

| Format | Contents | Byte exact replay |
| --- | --- | --- |
| `.raw` | Concatenated packet bytes | Yes |
| `.jsonl` | One JSON object per packet, preceded by a header line | Only with `--include-raw` |

A `.jsonl` trace starts with a header record that carries the format version, the dialect, the tool version, and the message and enum counts, so a trace stays interpretable after the tool is upgraded:

```json
{"record":"header","formatVersion":1,"createdUtc":"2026-01-01T00:00:00.0000000Z","dialect":"common","tool":"1.14.0","raw":true,"messageCount":221,"enumCount":136}
```

## A typical session

```bash
mavlinkx capture udp://:14550 -o flight.raw --duration 60
mavlinkx inspect flight.raw -v --limit 20
mavlinkx replay flight.raw --target udp://127.0.0.1:14551
mavlinkx capture udp://:14551 -o loopback.jsonl --duration 5 --include-raw -q
mavlinkx inspect loopback.jsonl
```