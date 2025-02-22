## Generators

Project which contains source generators required for NadekoBot project

---
### 1) Localized Strings Generator

    -- Why --
    Type safe response strings access, and enforces correct usage of response strings.

    -- How it works --
    Creates a file "strs.cs" containing a class called "strs" in "NadekoBot" namespace.
    
    Loads "strings/responses.en-US.json" and creates a property or a function for each key in the responses json file based on whether the value has string format placeholders or not.

    - If a value has no placeholders, it creates a property in the strs class which returns an instance of a LocStr struct containing only the key and no replacement parameters
    
    - If a value has placeholders, it creates a function with the same number of arguments as the number of placeholders, and passes those arguments to the LocStr instance

    -- How to use --
    1. Add a new key to responses.en-US.json "greet_me": "Hello, {0}"
    2. You now have access to a function strs.greet_me(obj p1)
    3. Using "GetText(strs.greet_me("Me"))" will return "Hello, Me"

