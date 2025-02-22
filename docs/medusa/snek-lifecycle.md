# Snek Lifecycle  

*You can override several methods to hook into command handler's lifecycle.  
These methods start with `Exec*`*


- `ExecOnMessageAsync` runs first right after any message was received  
- `ExecInputTransformAsync` runs after ExecOnMessageAsync and allows you to transform the message content before the bot looks for the matching command  
- `ExecPreCommandAsync` runs after a command was found but not executed, allowing you to potentially prevent command execution  
- `ExecPostCommandAsync` runs if the command was successfully executed  
- `ExecOnNoCommandAsync` runs instead of ExecPostCommandAsync if no command was found for a message  


*Besides that, sneks have 2 methods with which you can initialize and cleanup your snek*  


- `InitializeAsync` Runs when the medusa which contains this snek is being loaded  
- `DisposeAsync` Runs when the medusa which contains this snek is being unloaded  

