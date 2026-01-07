# LINK as a project

LINK is a custom protocol that allows a PC and a USB device to exchange data.

## The purpose

LINK can be *modified* as needed — only a few commands are common between all applications.  
With LINK, you separate your projects by "Application", so different LINK applications cannot communicate with each other (*as mentioned earlier, LINK can be modified, so this behavior can be changed if you want*).  
Each application is defined by an **APP-ID**.  

All commands follow this structure:  
**LINK:[APP-ID]:[COMMAND]:[ARGS_0]:[ARGS_1]:[ARGS_n]\0**  
Except for the **GETAPP** command, which is written as:  
**LINK:GETAPP\0**

The main commands are:
- **GETAPP**  
    - Used to retrieve the APP-ID. It can be disabled if needed.
- **GETV**  
    - Used to retrieve general information about the device.  
    - This command cannot be disabled, as it is required for software to detect LINK-compatible devices connected to the PC.
- **UPDATE**
    - Used to trigger the update function of the device.
    - This command cannot be disabled.
- **RETURN**  
    - Used to respond to a command. The first argument (<ARGS_0>) specifies which command it is responding to.  
    - This command cannot be disabled.

After this, you can add your own custom commands.

### Note for the format number for version
The version number follow this archirecture : `vX.Y.Z`
- X : is the LINK protocol compatibility, if this number change, then it means that the protocol has been changed.
- Y : is the app compatibility, if this number change, you would need to adapt your code (Client side or Device side).
- Z : is the patch changes, if this number change you would'nt need to change your code, is purpose is to pacth some bugs.

# LINK-Client

## Presentation

**LINK-Client** is a part of the LINK project.  
Its purpose is to provide a low-level communication layer, making it easier to create your own LINK-based software.  
LINK client versioning is provided through this format : `C-LINK_vX.Y.Z`

## Documentation
All of the documentation for the project can be found at [`.\docs\LINK_Architecture.md`](.\docs\LINK_Architecture.md)

# LEGALS

## License

This project is licensed under the [Apache License 2.0](./LICENSE).  
You may obtain a copy of the license at [http://www.apache.org/licenses/LICENSE-2.0](http://www.apache.org/licenses/LICENSE-2.0),  
or see the [LICENSE](./LICENSE) file for more details.

### Permissions
- Commercial use ✅  
- Modification ✅  
- Distribution ✅  
- Private use ✅  

### Conditions
- You must include the original copyright and license notice in any copies or substantial portions of the software.  
- If you modify the code, you must include a notice stating that you changed it.  
- No trademark use is granted.  

### Limitations
- The software is provided "as is", without warranty of any kind.  
- Liability is disclaimed for any damages resulting from the use of the software.
