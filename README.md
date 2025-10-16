# LINK as a project

LINK is a custom protocol who allows a PC and a USB device to exchange data.

## The purpose

LINK can be modified as needed, only few commands are common between each app.
With LINK you separate your project by "Application", this way differents LINK application cannot communicate together (as mentionned earlier, LINK can be modified, so it remains possible if you want). Each application is defined by an "APP-ID". All commands respect this architecture : LINK:<APP-ID>:<COMMAND>:<ARGS_0>:<ARGS_1>:<ARGS_n>\0
Except the "GETAPP" command who is written this way : LINK:GETAPP\0
The main commands are :
- GETAPP  (GETAPP is used to retrieve the APP-ID, it can be turned-off)
- GETV    (GETV is used to retrieve some uncountables about the device, it cannot be turned-off, cause that's the way softwares get the list of available device connected to the PC)
- RETURN  (RETURN is used to respond to a command, it's followed (in <ARGS_0>) by the command for who he is reponding, it cannot be turned off)
After this, you can add your own commands.

# LINK-Client

## Presentation

LINK-Client is a member of the "LINK" project.
His purpose his to give a low-level layer. It's usefull to create your own LINK software.

# LEGALS

## License

This project is licensed under the Apache License 2.0.  
You may obtain a copy of the license at [http://www.apache.org/licenses/LICENSE-2.0](http://www.apache.org/licenses/LICENSE-2.0).  

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
