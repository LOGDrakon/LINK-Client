# LINK Architecture Overview

LINK is a custom communication protocol designed to allow a PC and a USB-connected device (typically STM32-based) to exchange structured data efficiently.  
The project aims to be **modular**, **extensible**, and **cross-platform**, allowing easy integration across multiple transport layers (Serial, USB, CAN, TCP, etc.).

---

## Protocol Structure

All messages follow the same general structure:

`LINK:[APP-ID]:[COMMAND]:[ARGS_0]:[ARGS...]:[ARGS_n]\0`  

Each command belongs to a specific **Application**, identified by its `APP-ID`.  
This allows different LINK applications to coexist without interference.

---

## Standard Commands

These commands are common to all LINK-based devices.

### **GETAPP**
Retrieves the `APP-ID` of the device.  
Format:  
`LINK:GETAPP\0`  
- May be disabled in some implementations (optional command).

---

### **GETV**
Retrieves information about the device and the LINK version.  
Format:  
`LINK:[APP-ID]:GETV\0`  

- Always enabled — it is used by PC software to detect LINK-compatible devices.  
- Returns at least the **LINK version**.  
- Can be **extended** to include additional information such as:
  - Firmware version
  - MCU UID (e.g., STM32 unique ID)
  - Device model reference
  - Hardware revision, etc.

Example response:  
`LINK:DRAGON:RETURN:GETV:LINKv1.0:UID=0x12345678:MODEL=Dragon-Sensor\0`

---

### **RETURN**
Used by the device to respond to a command.  
Format:  
`LINK:[APP-ID]:RETURN:[COMMAND]:[ARGS_0]:[ARGS...]:[ARGS_n]\0`  
- `<COMMAND>` specifies which command is being answered.  
- This command is mandatory.

---

## Custom Commands

Applications can define their own commands freely.  
They must always include the APP-ID and follow the same syntax.  

Example:  
Client : `LINK:DRAGON:GETTEMP\0`  
Device : `LINK:DRAGON:RETURN:GETTEMP:24.6°C\0`  

---

# PC Software Design

The LINK software architecture on the PC side is divided into multiple layers (and corresponding NuGet packages).

---

## 1️. LINK.Core

**Purpose:**  
Implements the base protocol logic — parsing, frame building, command definitions.  
Contains no hardware-specific code.

**Main classes:**
- `LinkFrame` – Represents a LINK message.
- `LinkParser` – Handles message parsing and validation.
- `LinkCommand` – Defines standard LINK commands (`GETAPP`, `GETV`, `RETURN`).

**Namespace:** `Link.Core`

---

## 2️. LINK.Transport.Serial

**Purpose:**  
Provides a Serial/COM implementation of the LINK transport layer.  
Handles data transmission over USB-to-Serial bridges or virtual COM ports.

**Main classes:**
- `LinkSerialPort` – Wraps `SerialPort` or `Windows.Devices.SerialCommunication`.
- `LinkSerialTransport` – Converts raw data to/from `LinkFrame` using `LinkParser`.

**Namespace:** `Link.Transport.Serial`

---

## 3️. LINK.Client

**Purpose:**  
High-level API for interacting with LINK devices.  
Manages device discovery, connection, command sending, and response handling.

**Main classes:**
- `LinkDeviceInfo` – Contains info retrieved from `GETV` (version, UID, model, etc.)
- `LinkClient` – Provides async methods for command/response exchange.

**Namespace:** `Link.Client`

**Example methods:**
```csharp
Task ConnectAsync(string portName);
Task<LinkFrame> SendCommandAsync(string command, params string[] args);
Task<string> GetAppIdAsync();
Task<LinkDeviceInfo> GetDeviceInfoAsync();
```
## 4️. LINK.App.[AppName]

### Purpose:
Application-specific extensions built on top of LINK.Client.
Implements high-level functions related to a specific APP-ID.

**Example**: LINK.App.Dragon

**Example class**:  
```csharp
public class DragonClient : LinkClient
{
    public DragonClient() : base("DRAGON") { }

    public Task<string> GetSensorValueAsync()
        => SendCommandAsync("GETSENSOR").ContinueWith(t => t.Result.Arguments[0]);
}
```
## 5️. (Optional) LINK.Client.ExampleApp

### Purpose:
A demo or template project showing how to use the LINK Client API in a real application (Console, WinUI 3, etc.)

Structure example:
```
LINK.Client.ExampleApp/
 ├── Program.cs
 ├── MainWindow.xaml
 ├── MainWindow.xaml.cs
 └── Uses LINK.App.Dragon
```

## Package Hierarchy Summary
| Package                    | Description                            | Depends on            |
| -------------------------- | -------------------------------------- | --------------------- |
| **LINK.Core**              | Protocol format, parsing, common logic | —                     |
| **LINK.Transport.Serial**  | Serial (COM) implementation            | LINK.Core             |
| **LINK.Client**            | High-level API for LINK devices        | LINK.Transport.Serial |
| **LINK.App.[Name]**        | Application-specific implementation    | LINK.Client           |
| **LINK.Client.ExampleApp** | Demonstration / template app           | LINK.App.[Name]       |

## Design Principles
- Modular: Each layer can evolve independently.  
- Extendable: Supports new transports or applications easily.  
- Cross-compatible: Same logic applies to STM32 and PC implementations.  
- Discoverable: Devices expose themselves via GETV.  
- Future-proof: GETV and RETURN allow flexible extensions without breaking compatibility.  

## Notes

- The STM32-side “Device-LINK” library follows the same architecture and command conventions.
- The PC and Device libraries are meant to evolve in parallel — both speak the same LINK protocol.
- Each LINK App-ID represents a self-contained application domain (e.g., “DRAGON”, “SENSOR”, “BOOTLOADER”, etc.).

© 2025 — LINK Project
Licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
