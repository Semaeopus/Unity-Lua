using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using MoonSharp.VsCodeDebugger;
using MoonSharp.Interpreter.Loaders;
using System.Linq;
using System.Reflection;

/// <summary>
/// Wrapper around the Moonsharp API.
/// Implements heavy error checking and logging for end users
/// </summary>
public class LuaVM
{
    /// <summary>
    /// The Moonsharp script object
    /// </summary>
    private readonly Script                     m_LuaScript     = null;
    private static Type[]                       m_APITypeList   = null;
	private LuaAPIBase[]	                    m_APIList		= null;
    private static DynValue                     m_EnumTables    = null;
    
    /// <summary>
    /// Settings to control the behaviour of the VM
    /// </summary>
    [Flags]
    public enum VMSettings : uint
    {
        /// <summary>
        /// No custom code will be attached
        /// </summary>
        None            = 0,
        
        /// <summary>
        /// We'll attach anything deriving from LuaAPIBase
        /// </summary>
        AttachAPIs      = 1 << 0,
        
        /// <summary>
        /// We'll attach any enum with the LuaApiEnum attribute
        /// </summary>
        AttachEnums     = 1 << 1,
        
        /// <summary>
        /// Attach everything we know about
        /// </summary>
        AttachAll       = ~0u,
    }

    /// <summary>
    /// The Moonsharp remote debugger service
    /// </summary>
	private static MoonSharpVsCodeDebugServer   s_RemoteDebugger = null;
    
    /// <summary>
    /// Default to attaching all apis and enums
    /// </summary>
    public LuaVM()
        : this(VMSettings.AttachAll)
    {
    }
    
    /// <summary>
    /// Default settings are a soft sandbox and setting up the file system script loader
    /// </summary>
    public LuaVM(VMSettings vmSettings)
    {
        m_LuaScript =
            new Script(CoreModules.Preset_SoftSandbox)
            {
                Options =
                {
                    ScriptLoader = new FileSystemScriptLoader(),
                    DebugPrint = log => Logger.Log (Channel.LuaNative, log),
                }
            };

        if ((vmSettings & VMSettings.AttachAPIs) == VMSettings.AttachAPIs)
        {
            AttachAPIS();
        }
        
        if ((vmSettings & VMSettings.AttachEnums) == VMSettings.AttachEnums)
        {
            AttachLuaEnums();
        }
    }

    /// <summary>
    /// Add a new global Lua table and if successful return a reference to it
    /// </summary>
    /// <param name="tableName"></param>
    /// <returns></returns>
    public Table AddGlobalTable(string tableName)
    {
        Table table = null;

        if(SetGlobal(tableName, DynValue.NewTable(m_LuaScript)))
        {
            table = GetGlobal(tableName).Table;
        }
        else
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Failed to add global Lua table {0}", tableName);
        }

        return table;
    }

    /// <summary>
    /// Attempts to set a global variable with key and value  
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns>true if value was set successfully</returns>
    public bool SetGlobal(string key, object value)
    {
        bool didSet = false;
        try
        {
            m_LuaScript.Globals[key] = value;
            didSet = true;
        }
        catch (InterpreterException ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Lua SetGlobal error: {0}", ex.DecoratedMessage);
        }
        return didSet;
    }

    /// <summary>
    /// Attempts to retrive a value from the Lua globals
    /// </summary>
    /// <param name="key"></param>
    /// <returns>null if failure occurs, else the requested value as a DynValue</returns>
    public DynValue GetGlobal(string key)
    {
        DynValue result = DynValue.Nil;
        try
        {
            result = m_LuaScript.Globals.Get(key);
        }
        catch
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Failed to get Lua global {0}", key);
        }

        return result;
    }

	/// <summary>
	/// Attempts to retrive a value from the Lua globals, allowing the user to pass parent and children names in
	/// </summary>
	/// <returns>The global.</returns>
	/// <param name="keys">Keys.</param>
	public DynValue GetGlobal(params object[] keys)
	{
        DynValue result = DynValue.Nil;
		try
		{
			result = m_LuaScript.Globals.Get(keys);
		}
		catch
		{
			Logger.Log(Channel.Lua, Priority.FatalError, "Failed to get Lua global at '{0}'", 
			    string.Join(", ", Array.ConvertAll(keys, input => input.ToString())));
		}

		return result;
	}

	/// <summary>
	/// Attempts to retrive a table from the Lua globals
	/// </summary>
	/// <returns>The global table.</returns>
	/// <param name="key">Key.</param>
	public Table GetGlobalTable(string key)
	{
		Table result = null;
		DynValue tableDyn = GetGlobal (key);
		if (tableDyn != null)
		{
			if(tableDyn.Type == DataType.Table)
			{
				result = tableDyn.Table;
			}
			else
			{
				Logger.Log(Channel.Lua, Priority.FatalError, "Lua global {0} is not type table, has type {1}", key, tableDyn.Type.ToString());
			}
		}
		return result;
	}

	/// <summary>
	/// Attempts to retrive a table from the Lua globals, allowing the user to pass parent and children names in
	/// </summary>
	/// <returns>The global table.</returns>
	/// <param name="keys">Key.</param>
	public Table GetGlobalTable(params object[] keys)
	{
		Table result = null;
		DynValue tableDyn = GetGlobal (keys);
		if (tableDyn != null)
		{
			if(tableDyn.Type == DataType.Table)
			{
				result = tableDyn.Table;
			}
			else
			{
				Logger.Log(Channel.Lua, Priority.FatalError, "Lua global {0} is not type table, has type {1}", keys, tableDyn.Type.ToString());
			}
		}
		return result;
	}

    /// <summary>
    /// Return the global table for this vm
    /// </summary>
    /// <returns></returns>
    public Table GetGlobalsTable()
    {
        return m_LuaScript.Globals;
    }

    /// <summary>
    /// Attempts to run the lua command passed in
    /// </summary>
    /// <param name="command"></param>
    /// <returns>Null if an error occured otherwise will return the result of the executed lua code</returns>
    public DynValue ExecuteString(string command)
    {
        DynValue result = DynValue.Nil;

        try
        {
            result = m_LuaScript.DoString(command);
        }
        catch (InterpreterException ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Lua ExecuteString error: {0}", ex.DecoratedMessage);
        }

        return result;
    }

    /// <summary>
    /// Attempts to run the lua script passed in
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns>Null if an error occured otherwise will return the result of the executed lua code</returns>
    public DynValue ExecuteScript(string filePath)
    {
        DynValue result = DynValue.Nil;

        try
        {
            result = m_LuaScript.DoFile(filePath);
        }
        catch (InterpreterException ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Lua ExecuteScript error: {0}", ex.DecoratedMessage);
        }
        catch (Exception ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "System ExecuteScript error: {0}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Attempts to load a lua script
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns>Null if an error occured otherwise will return the DynValue of the script. This can be passed to Call()</returns>
    public DynValue LoadScript(string filePath)
    {
        DynValue result = DynValue.Nil;

        try
        {
            result = m_LuaScript.LoadFile(filePath);
        }
        catch (InterpreterException ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Lua ExecuteString error: {0}", ex.DecoratedMessage);
        }
        catch (Exception ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "System ExecuteString error: {0}", ex.Message);
        }

        return result;
    }
    
    /// <summary>
    /// Attemps to load a string containing lua code
    /// </summary>
    /// <param name="luaString"></param>
    /// <returns>Null if an error occured otherwise will return the DynValue of the script. This can be passed to Call()</returns>
    public DynValue LoadString(string luaString)
    {
        DynValue result = DynValue.Nil;

        try
        {
            result = m_LuaScript.LoadString(luaString);
        }
        catch (InterpreterException ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Lua ExecuteString error: {0}", ex.DecoratedMessage);
        }
        catch (Exception ex)
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "System ExecuteString error: {0}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Call a lua function via DynValue
    /// </summary>
    /// <param name="luaFunc"></param>
    /// <param name="args"></param>
    /// <returns>Null if call fails of function is invalid, else the result of the function</returns>
    public DynValue Call(DynValue luaFunc, params object[] args)
    {
        DynValue result = DynValue.Nil;

        if (luaFunc.IsNotNil() && luaFunc.Type == DataType.Function)
        {
            try
            {
                result = m_LuaScript.Call(luaFunc, args);
            }
            catch (ScriptRuntimeException ex)
            {
                Logger.Log(Channel.Lua, Priority.FatalError, "Lua Call error: {0}", ex.DecoratedMessage);
            }
        }
        else
        {
            Logger.Log(Channel.Lua, Priority.FatalError, "Invalid lua function passed to LuaVM::Call");
        }

        return result;
    }

    /// <summary>
    /// Call a lua function via name
    /// </summary>
    /// <param name="functionName">Function name.</param>
    /// <param name="args">Arguments.</param>
    public DynValue Call(string functionName, params object[] args)
    {
        DynValue result = DynValue.Nil;

        if (!string.IsNullOrEmpty(functionName))
        {
            DynValue func = GetGlobal(functionName);
            if (func.Type == DataType.Function)
            {
                try
                {
                    result = m_LuaScript.Call(func, args);
                }
                catch (InterpreterException ex)
                {
                    Logger.Log(Channel.Lua, Priority.FatalError, "Lua error calling function {0}: {1}", functionName, ex.DecoratedMessage);
                }
            }
            else
            {
                Logger.Log(Channel.Lua, Priority.FatalError, "Failed to find lua function '{0}'", functionName);
            }
        }
        return result;
    }

    /// <summary>
    /// Starts the remote debugger and opens the interface in the users browser
    /// </summary>
    public static void StartRemoteDebugger()
    {
        if (s_RemoteDebugger == null)
        {
			s_RemoteDebugger = new MoonSharpVsCodeDebugServer();
			s_RemoteDebugger.Start ();
        }
    }

    /// <summary>
    /// Attaches to the remove debugger service
    /// </summary>
    public void AttachDebugger()
    {
        if(s_RemoteDebugger != null)
        {
			s_RemoteDebugger.AttachToScript (m_LuaScript, "Lua script");
        }
        else
        {
            Logger.Log(Channel.Lua, Priority.Error, "Tried to attach script to debugger before debugger was started");
        }
    }
    
    /// <summary>
    /// Return the current script object
    /// </summary>
    public Script GetScriptObject()
    {
        return m_LuaScript;
    }

    /// <summary>
    /// Create static api list and attach to this VM
    /// </summary>
    private void AttachAPIS()
    {
        if (m_APITypeList == null)
        {
            List<Type> apiTypeList = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                apiTypeList.AddRange(assembly.GetTypes()
                    .Where(type => type.IsSubclassOf(typeof(LuaAPIBase))));
            }

            m_APITypeList = apiTypeList.ToArray();
        }

        m_APIList = new LuaAPIBase[m_APITypeList.Length];

        for (int i = 0; i < m_APITypeList.Length; ++i)
        {
            m_APIList[i] = Activator.CreateInstance(m_APITypeList[i]) as LuaAPIBase;
        }
        
        // Iterate apis and tell them to update this lua vm
        foreach (LuaAPIBase api in m_APIList)
        {
            api.AddAPIToLuaInstance(this);
        }
    }

    /// <summary>
    /// Create the reusable enum prime table and attach to this VM
    /// </summary>
    private void AttachLuaEnums()
    {
        if (m_EnumTables == null)
        {
            // Create a new prime table
            // Prime tables can be shared between scripts 
            m_EnumTables = DynValue.NewPrimeTable();
    
            // Get all enums with the lua attribute
            List<Type> luaEnumList = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                luaEnumList.AddRange((assembly.GetTypes()
                    .Where(luaEnumType => Attribute.IsDefined(luaEnumType, typeof(LuaApiEnum)))));
            }
            
            foreach (Type enumType in luaEnumList)
            {
                // Get the attribute
                LuaApiEnum apiEnumAttrib = (LuaApiEnum) enumType.GetCustomAttributes(typeof(LuaApiEnum), false)[0];

                // Create the table for this enum and get a reference to it 
                m_EnumTables.Table.Set(apiEnumAttrib.name, DynValue.NewPrimeTable());
                Table enumTable = m_EnumTables.Table.Get(apiEnumAttrib.name).Table;
                
                // Foreach value in the enum list
                foreach (var enumValue in Enum.GetValues(enumType))
                {
                    var memberInfo = enumType.GetMember(enumValue.ToString());
                    var attribute = memberInfo[0].GetCustomAttributes(typeof(LuaApiEnumValue), false);

                    // Double check they've not been flagged as hidden
                    if (attribute.Length > 0 && ((LuaApiEnumValue) attribute[0]).hidden)
                    {
                        continue;
                    }
                    
                    enumTable.Set(enumValue.ToString(), DynValue.NewNumber((int) enumValue));
                }
            }
        }
        
        // Iterate through the enum cache and copy the values into our globals
        foreach (var enumPair in m_EnumTables.Table.Pairs)
        {
            m_LuaScript.Globals.Set(enumPair.Key, enumPair.Value);
        }
    }
}
