using MoonSharp.Interpreter;

/// <summary>
/// Base class for all lua api systems
/// </summary>
public abstract class LuaAPIBase 
{
	/// <summary>
	/// The table containing the API functions and variables
	/// </summary>
	protected Table  m_ApiTable;

    /// <summary>
    /// The luaVM the api is attached to
    /// </summary>
	protected LuaVM  m_ParentVM;

	/// <summary>
	/// The name of the Lua API, this will be used for the api name in Lua
	/// E.G
	/// 
	/// C#:
	/// m_APIName = "ExampleAPI";
	/// 
	/// Lua:
	/// ExampleAPI.ExampleFunction()
	/// </summary>
	private readonly string m_APIName;

	/// <summary>
	/// Derived types must provide their name
	/// </summary>
	/// <param name="APIName">API name.</param>
	protected LuaAPIBase(string APIName)
	{
		m_APIName = APIName;
	}

	/// <summary>
	/// Derived types must create a function that fills in the api table
	/// </summary>
	protected abstract void InitialiseAPITable();

	/// <summary>
	/// Adds the API to lua instance.
	/// </summary>
	/// <param name="luaInstance">Lua instance.</param>
	public void AddAPIToLuaInstance(LuaVM luaInstance)
	{
        // Set our parent
        m_ParentVM  = luaInstance;

		// Make a new table
		Table   apiTable = m_ParentVM.AddGlobalTable (m_APIName);
		if (apiTable != null)
		{
			// Set the api table
			m_ApiTable = apiTable;

			// Hand over to the API derived type to fill in the table
			InitialiseAPITable();
		} 
		else
		{
			Logger.Log (Channel.Lua, "Failed to Initilise API {0}", m_APIName);
		}
	}
}
