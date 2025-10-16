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

### `GETAPP`
Retrieves the `APP-ID` of the device.  

**Format:**  
`LINK:GETAPP\0`
- May be disabled in some implementations (optional command).

### `GETV`
Retrieves information about the device and the LINK version.

**Format:**  
`LINK:[APP-ID]:GETV\0`  

- Always enabled — it is used by PC software to detect LINK-compatible devices.  
- Returns at least the **LINK version**.  
- Can be **extended** to include additional information such as:
  - Firmware version
  - MCU UID (e.g., STM32 unique ID)
  - Device model reference
  - Hardware revision, etc.
  - May include security-related information (e.g., `LOCKED=true`, `ENC=AES128`).

**Example response:**  
`LINK:DRAGON:RETURN:GETV:LINKv1.0:UID=0x12345678:MODEL=Dragon-Sensor\0`

### `RETURN`
Used by the device to respond to a command.  

**Format:**  
`LINK:[APP-ID]:RETURN:[COMMAND]:[ARGS_0]:[ARGS...]:[ARGS_n]\0`  
- `<COMMAND>` specifies which command is being answered.  
- This command is mandatory.

## Custom Commands

Applications can define their own commands freely.  
They must always include the APP-ID and follow the same syntax.  

**Example:**  
Client : `LINK:DRAGON:GETTEMP\0`  
Device : `LINK:DRAGON:RETURN:GETTEMP:24.6°C\0`  

---

# PC Software Design

The LINK software architecture on the PC side is divided into multiple layers (and corresponding NuGet packages).

## 1️. LINK.Core

**Namespace:** `Link.Core`

### Purpose:
Implements the base protocol logic — parsing, frame building, command definitions.  
Contains no hardware-specific code.

### Security support:
- Defines optional crypto interfaces for encryption/decryption modules.  
- Encryption can be implemented in higher layers (e.g., `LINK.Client`) using this interface.

### Main classes:
- `LinkFrame` – Represents a LINK message.  
- `LinkParser` – Handles message parsing and validation.  
- `LinkCommand` – Defines standard LINK commands (`GETAPP`, `GETV`, `RETURN`).  
- `ILinkCryptoProvider` – Interface for pluggable encryption backends (AES, etc.)  

## 2️. LINK.Transport.Serial

**Namespace:** `Link.Transport.Serial`

### Purpose:  
Provides a Serial/COM implementation of the LINK transport layer.  
Handles data transmission over USB-to-Serial bridges or virtual COM ports.

### Main classes:
- `LinkSerialPort` – Wraps `SerialPort` or `Windows.Devices.SerialCommunication`.
- `LinkSerialTransport` – Converts raw data to/from `LinkFrame` using `LinkParser`.

## 3️. LINK.Client

**Namespace:** `Link.Client`

### Purpose:
High-level API for interacting with LINK devices.  
Manages device discovery, connection, command sending, and response handling.

### Security support:
- Can authenticate with a device using a password (`AUTH` command).  
- Supports optional encryption modes when both sides agree on an algorithm.  
- Exposes security state through `LinkDeviceInfo` (e.g., `IsLocked`, `EncryptionMode`).

### Main classes:
- `LinkDeviceInfo` – Contains info retrieved from `GETV` (version, UID, model, etc.)
- `LinkClient` – Provides async methods for command/response exchange.
- `LinkSecurityManager` – Handles authentication and encryption initialization.  

**Example methods:**
```csharp
Task ConnectAsync(string portName);
Task<LinkFrame> SendCommandAsync(string command, params string[] args);
Task<string> GetAppIdAsync();
Task<LinkDeviceInfo> GetDeviceInfoAsync();
Task<bool> AuthenticateAsync(string password);
bool IsAuthenticated { get; }
```

**Example class:**
```csharp
public class LinkDeviceInfo
{
    public string AppId { get; set; }
    public string Version { get; set; }
    public string Uid { get; set; }
    public string Model { get; set; }
    public bool IsLocked { get; set; }
    public string EncryptionMode { get; set; } // e.g. NONE, AES128
}
```

## 4️. LINK.App.[AppName]

**Namespace example**: `LINK.App.Dragon`

### Purpose:
Application-specific extensions built on top of LINK.Client.
Implements high-level functions related to a specific APP-ID.

**Example class:**  
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

**Structure example:**
```
LINK.Client.ExampleApp/
 ├── Program.cs
 ├── MainWindow.xaml
 ├── MainWindow.xaml.cs
 └── Uses LINK.App.Dragon
```

---

# Resume

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
- Secure: Supports optional authentication and encryption layers, ensuring safe communication with LINK devices.
- Future-proof: GETV and RETURN allow flexible extensions without breaking compatibility.

## Security Options

LINK supports optional security mechanisms that can be enabled per device or application.

### 1. Password Protection (Authentication)

Devices can require a password before certain commands are accepted.  
This feature allows locking sensitive actions such as configuration changes or firmware updates.

**Command:**  
`LINK:[APP-ID]:AUTH:[PASSWORD]\0`  
**Response:**  
`LINK:[APP-ID]:RETURN:AUTH:OK\0`  
`LINK:[APP-ID]:RETURN:AUTH:ERR\0` 

- When authentication is enabled, the device starts in a **LOCKED** state.  
- Only `GETAPP` and `GETV` remain accessible before authentication.  
- Once unlocked, all commands for the given APP-ID are enabled.  
- The password can be stored in the device configuration or derived from hardware (e.g., UID-based key).

### **2. Encryption Support**

LINK can optionally encrypt its communication layer to ensure confidentiality and integrity.  
Encryption is configured per device and announced in `GETV`.

- The encryption mode may be `NONE`, `AES128`, or any other supported algorithm.  
- Both sides must agree on the mode and key before communication.  

Example `GETV` extended response:
`LINK:DRAGON:RETURN:GETV:LINKv1.1:UID=0x12345678:MODEL=Dragon-Sensor:ENC=AES128:LOCKED=true\0`

### **3. STM32 Compatibility and Software Crypto**

Not all STM32 MCUs include a hardware cryptographic accelerator.  
For this reason, the Device-LINK library can embed a lightweight **software crypto module** implementing the same encryption algorithms as the PC side (e.g., AES in software).

- When a hardware accelerator is present, the library automatically switches to hardware mode.  
- When unavailable, it falls back to the software crypto layer.  
- This ensures **consistent behavior** across all STM32 families.

The software crypto component is delivered as a small optional module, keeping compatibility and reducing maintenance complexity.

### **4. Configuration Summary**

| Feature | Description | Mandatory | Typical Use |
|----------|--------------|------------|--------------|
| `AUTH` | Password-based authentication | No | Device access control |
| `ENCRYPTION` | Encrypted communication | No | Protect data exchanges |
| `SW_CRYPTO` | Software crypto fallback | No | For STM32 without HW crypto |

## Notes

- The STM32-side “Device-LINK” library follows the same architecture and command conventions.
- The PC and Device libraries are meant to evolve in parallel — both speak the same LINK protocol.
- Each LINK App-ID represents a self-contained application domain (e.g., “DRAGON”, “SENSOR”, “BOOTLOADER”, etc.).

---

© 2025 — LINK Project  
Licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)  
By [LOGDrakon](https://github.com/LOGDrakon)
