#if UNITY_EDITOR
using System;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Handles generating Lua documentation for varying sources via reflection
/// </summary>
public static class LuaDocGenerator
{
    /// <summary>
    /// Info about a Lua API
    /// </summary>
    private class LuaApiInfo
    {
        public LuaApi Attribute;
        public Type Type;

        public readonly List<LuaFunction_Info> functions = new List<LuaFunction_Info>();
        public readonly List<LuaVariableInfo> variables = new List<LuaVariableInfo>();
    }

    /// <summary>
    /// Info about a Lua function
    /// </summary>
    private class LuaFunction_Info
    {
        public LuaApiFunction Attribute;
        public MethodInfo MethodInfo;
    }

    /// <summary>
    /// Info about a Lua variable
    /// </summary>
    private class LuaVariableInfo
    {
        public LuaApiVariable Attribute;
        public FieldInfo FieldInfo;
    }

    /// <summary>
    /// Info about a Lua enum
    /// </summary>
    private class LuaEnumInfo
    {
        public LuaApiEnum Attribute;
        public Type ApiType;
        public readonly List<LuaEnumValueInfo> values = new List<LuaEnumValueInfo>();
    }

    /// <summary>
    /// Info about a Lua enum value
    /// </summary>
    private class LuaEnumValueInfo
    {
        public LuaApiEnumValue Attribute;
        public string StringValue;
    }

    /// <summary>
    /// Grabs information about all the Lua Apis in the code base
    /// </summary>Å
    /// <returns></returns>
    private static List<LuaApiInfo> GetAllApiInfo()
    {
        List<Type> apiTypeList = new List<Type>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            apiTypeList.AddRange(assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(LuaAPIBase))));
        }
        
        List<LuaApiInfo> apiInfoArray = new List<LuaApiInfo>(apiTypeList.Count);

        // Iterate all types deriving from LuaApiBase
        foreach (Type apiType in apiTypeList)
        {
            LuaApi[] apiAttribs = apiType.GetCustomAttributes(typeof(LuaApi), false) as LuaApi[];
            if (apiAttribs != null && apiAttribs.Length > 0)
            {
                LuaApiInfo apiInfo = new LuaApiInfo
                {
                    Type = apiType,
                    Attribute = apiAttribs[0]
                };

                // Iterate all methods
                foreach(MethodInfo methodType in apiType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
                {
                    // Grab the LuaApiFunction if it exists
                    LuaApiFunction[] functionAttribs = methodType.GetCustomAttributes(typeof(LuaApiFunction), false) as LuaApiFunction[];

                    if (functionAttribs != null && functionAttribs.Length > 0)
                    {
                        apiInfo.functions.Add(new LuaFunction_Info
                            {
                                Attribute = functionAttribs[0],
                                MethodInfo = methodType,
                            });
                    }
                }

                // Iterate all variables
                foreach(FieldInfo fieldInfo in apiType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.Public | BindingFlags.Static))
                {
                    LuaApiVariable[] varaibleAttribs =
                        fieldInfo.GetCustomAttributes(typeof(LuaApiVariable), false) as LuaApiVariable[];

                    if (varaibleAttribs != null && varaibleAttribs.Length > 0)
                    {
                        apiInfo.variables.Add(new LuaVariableInfo{
                            Attribute = varaibleAttribs[0],
                            FieldInfo =  fieldInfo,
                        });
                    }
                }

                apiInfoArray.Add(apiInfo);
            }
        }

        return apiInfoArray;
    }

    /// <summary>
    /// Grabs all information about enums that will be bound to Lua
    /// </summary>
    /// <returns></returns>
    private static List<LuaEnumInfo> GetAllEnums()
    {
        List<Type> luaEnumList = new List<Type>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            luaEnumList.AddRange((assembly.GetTypes()
                .Where(luaEnumType => Attribute.IsDefined(luaEnumType, typeof(LuaApiEnum)))));
        }
        
        List<LuaEnumInfo> result = new List<LuaEnumInfo>(luaEnumList.Count);

        foreach (Type enumType in luaEnumList)
        {
            LuaEnumInfo enumInfo = new LuaEnumInfo
            {
                ApiType = enumType,
                Attribute = (LuaApiEnum) enumType.GetCustomAttributes(typeof(LuaApiEnum), false)[0],
            };

            foreach (var enumVal in Enum.GetValues(enumType))
            {
                var valueInfo = enumType.GetMember(enumVal.ToString());
                var attribs = valueInfo[0].GetCustomAttributes(typeof(LuaApiEnumValue), false);
                enumInfo.values.Add(new LuaEnumValueInfo
                {
                    Attribute = (LuaApiEnumValue)(attribs.Length > 0 ? attribs[0] : null),
                    StringValue = enumVal.ToString(),
                });
            }

            result.Add(enumInfo);
        }

        return result;
    }

    /// <summary>
    /// Generate MediaWiki pages for all Lua apis and enums
    /// </summary>
    [MenuItem("Lua/Docs/Generate MediaWiki Docs")]
    private static void GenerateMediaWikiDocs()
    {
        Logger.Log (Channel.Lua, "Beginning Lua doc generation for MediaWiki");

        string documentationPath =
            EditorUtility.OpenFolderPanel("Choose a location to place wiki files", string.Empty, string.Empty);

        if (!string.IsNullOrEmpty(documentationPath))
        {
            // Iterate all types deriving from LuaApiBase
            foreach (LuaApiInfo api in GetAllApiInfo())
            {
                // Write out the header for the api type
                LuaApi luaApiDetails = api.Attribute;

                string apiDocPath = string.Format("{0}/{1}.mediawiki", documentationPath, luaApiDetails.luaName);

                StreamWriter documentation = File.CreateText(apiDocPath);

                Logger.Log(Channel.Lua, "Generating documentation for api: {0}", luaApiDetails.luaName);

                documentation.WriteLine("= {0} =", luaApiDetails.luaName);

                if (!string.IsNullOrEmpty(luaApiDetails.description))
                {
                    documentation.WriteLine("== Description ==");
                    documentation.WriteLine(luaApiDetails.description);
                }
                if (!string.IsNullOrEmpty(luaApiDetails.notes))
                {
                    documentation.WriteLine("== Notes ==");
                    documentation.WriteLine(luaApiDetails.notes);
                }


                documentation.WriteLine("== Functions ==");

                // Iterate all methods
                foreach (LuaFunction_Info method in api.functions)
                {
                    // Grab the LuaApiFunction if it exists
                    LuaApiFunction luaMethodDetails =  method.Attribute;

                    Logger.Log(Channel.Lua, "\t {0}", luaMethodDetails.name);

                    documentation.WriteLine("=== {0} ===", luaMethodDetails.name);

                    string functionSig = GetCleanFunctionSignature(method.MethodInfo, luaMethodDetails, luaApiDetails);

                    documentation.WriteLine("<syntaxhighlight source lang=\"lua\">{0}</syntaxhighlight>", functionSig);

                    bool hasParams = method.MethodInfo.GetParameters().Length != 0;

                    if (hasParams)
                    {
                        documentation.WriteLine("'''Expected parameter types'''");
                        documentation.WriteLine("{| class=\"wikitable\"");
                    }

                    // Expected param types
                    foreach (ParameterInfo param in method.MethodInfo.GetParameters())
                    {
                        documentation.WriteLine("|-");
                        documentation.WriteLine("| {0} || {1}", param.Name, GetCleanTypeName(param.ParameterType));
                    }

                    if (hasParams)
                    {
                        documentation.WriteLine("|}");
                    }

                    documentation.WriteLine("'''Description''': {0}\n", luaMethodDetails.description);

                    // Use custom return description if exists, else generate one
                    documentation.WriteLine("'''Returns''': {0}\n",
                        !string.IsNullOrEmpty(luaMethodDetails.returns)
                        ? luaMethodDetails.returns
                        : GetCleanTypeName(method.MethodInfo.ReturnType));

                    if (!string.IsNullOrEmpty(luaMethodDetails.notes))
                    {
                        documentation.WriteLine("'''Notes''': {0}",
                            luaMethodDetails.notes);
                    }

                    if (!string.IsNullOrEmpty(luaMethodDetails.warning))
                    {
                        documentation.WriteLine("<span style=\"color:#ff0000\">'''Warning'''</span>: {0}",
                            luaMethodDetails.warning);
                    }

                    if (!string.IsNullOrEmpty(luaMethodDetails.success))
                    {
                        documentation.WriteLine("<span style=\"color:#009000\">'''Tip'''</span>: {0}",
                            luaMethodDetails.success);
                    }
                }

                bool wroteTitle = false;
                foreach (LuaVariableInfo fieldInfo in api.variables)
                {
                    if (!wroteTitle)
                    {
                        documentation.WriteLine("== Variables ==");
                        wroteTitle = true;
                    }

                    LuaApiVariable luaVariableDetails = fieldInfo.Attribute;

                    documentation.WriteLine("=== {0} ===", luaVariableDetails.name);

                    string varibleSig = string.Format("{0}.{1}", luaApiDetails.luaName, luaVariableDetails.name);
                    documentation.WriteLine("<syntaxhighlight source lang=\"lua\">{0}</syntaxhighlight>", varibleSig);

                    documentation.WriteLine("'''Description''': {0}\n", luaVariableDetails.description);
                }

                // Add time stamp
                documentation.WriteLine("\n\n'''Docs last hacked together on''': {0:dd/MM/yyyy H:mm}", DateTime.Now);

                documentation.Close();
            }

            // TODO Enum docs

            string enumDocPath = string.Format("{0}/Constants.mediawiki", documentationPath);

            StreamWriter enumDocumentation = File.CreateText(enumDocPath);

            enumDocumentation.WriteLine("= Constants =");

            enumDocumentation.WriteLine("== Enums ==");
            foreach (LuaEnumInfo enumInfo in GetAllEnums())
            {
                enumDocumentation.WriteLine("=== {0} ===", enumInfo.Attribute.name);
                if (!string.IsNullOrEmpty(enumInfo.Attribute.description))
                {
                    enumDocumentation.WriteLine(enumInfo.Attribute.description);
                }
                
                enumDocumentation.WriteLine("{| class=\"wikitable\"");
                enumDocumentation.WriteLine("|-");
                enumDocumentation.WriteLine("! Usage !! Description");

                foreach (LuaEnumValueInfo value in enumInfo.values)
                {
                    if (value.Attribute != null && value.Attribute.hidden)
                    {
                        continue;
                    }
                    enumDocumentation.WriteLine("|-");
                    enumDocumentation.WriteLine("| {0}.{1} || {2}",
                        enumInfo.Attribute.name,
                        value.StringValue,
                        value.Attribute != null ? value.Attribute.description : string.Empty);
                }                
                        
                enumDocumentation.WriteLine("|}");
            }
            
            enumDocumentation.Close();
        }

        Logger.Log (Channel.Lua, "Completed Lua doc generation");
    }

    private class VSCodeSnippet
    {
        public string     prefix;
        public string[]   body;
        public string     description;
    }

    [MenuItem("Lua/Docs/Generate VSCode Snippets")]
    public static void GenerateVSCodeSnippets()
    {
        StringBuilder snippets = new StringBuilder();
        VSCodeSnippet snippet  = new VSCodeSnippet {body = new string[1]};

        foreach (LuaApiInfo api in GetAllApiInfo())
        {
            // Write out the header for the api type
            LuaApi luaApiDetails = api.Attribute;

            // Iterate all methods
            foreach (LuaFunction_Info method in api.functions)
            {
                // Grab the LuaApiFunction if it exists
                LuaApiFunction luaMethodDetails = method.Attribute;

                snippet.prefix = string.Format("{0}.{1}", luaApiDetails.luaName, luaMethodDetails.name);

                string paramString = string.Empty;

                var paramArray = method.MethodInfo.GetParameters();
                for (int i = 0; i < paramArray.Length; i++)
                {
                    var param = paramArray[i];

                    paramString += string.Format("${{{0}:{1}}}, ", i, param.Name);
                }

                if (!string.IsNullOrEmpty(paramString))
                {
                    paramString = paramString.Remove(paramString.Length - 2, 2);
                }

                snippet.body[0] = string.Format("{0}({1})", snippet.prefix, paramString);

                snippet.description = luaMethodDetails.description;

                string finalBlock = string.Format("\"{0}\": {1},", snippet.prefix, JsonUtility.ToJson(snippet, true));

                snippets.AppendLine(finalBlock);
            }

            foreach (LuaVariableInfo fieldInfo in api.variables)
            {
                LuaApiVariable luaVariableDetails = fieldInfo.Attribute;

                snippet.prefix = string.Format("{0}.{1}", luaApiDetails.luaName, luaVariableDetails.name);
                snippet.body[0] = snippet.prefix;
                snippet.description = luaVariableDetails.description;

                string finalBlock = string.Format("\"{0}\" : {1},", snippet.prefix, JsonUtility.ToJson(snippet, true));

                snippets.AppendLine(finalBlock);
            }
        }

        EditorGUIUtility.systemCopyBuffer = snippets.ToString();
    }

    private class AtomSnippet
    {
        public string     Prefix;
        public string     Body;
        public string     Description;
        public string     DescriptionMoreUrl;

        private static string ToLiteral(string input)
        {
            input = input.Replace("'", @"\'");
            input = input.Replace("\"", @"""");
            return input;
        }

        public string ConstructCSONString()
        {
            return string.Format(@"
  '{0}':
    'prefix': '{0}'
    'body': '{1}'
    'description' : '{2}'
    'rightLabelHTML': '<span style=""color:#DC9656"">Lua</span>'
    'descriptionMoreURL' : '{3}'",
            Prefix,
            ToLiteral(Body),
            ToLiteral(Description),
            DescriptionMoreUrl);

        }
    }

    [MenuItem("Lua/Docs/Generate Atom Snippets")]
    public static void GenerateAtomSnippets()
    {
        StringBuilder snippets = new StringBuilder();
        AtomSnippet snippet  = new AtomSnippet();

        snippets.AppendLine("'.source.lua':");

        // Iterate all types deriving from LuaApiBase
        foreach (LuaApiInfo api in GetAllApiInfo())
        {
            // Write out the header for the api type
            LuaApi luaApiDetails = api.Attribute;

            // Iterate all methods
            foreach (LuaFunction_Info method in api.functions)
            {
                // Grab the LuaApiFunction if it exists
                LuaApiFunction luaMethodDetails = method.Attribute;

                snippet.Prefix = string.Format("{0}.{1}", luaApiDetails.luaName, luaMethodDetails.name);

                string paramString = string.Empty;

                var paramArray = method.MethodInfo.GetParameters();
                for (int i = 0; i < paramArray.Length; i++)
                {
                    var param = paramArray[i];

                    paramString += string.Format("${{{0}:{1}}}, ", i, param.Name);
                }

                if (!string.IsNullOrEmpty(paramString))
                {
                    paramString = paramString.Remove(paramString.Length - 2, 2);
                }

                snippet.Body = string.Format("{0}({1})", snippet.Prefix, paramString);

                snippet.Description = luaMethodDetails.description;

                snippets.AppendLine(snippet.ConstructCSONString());
            }

            foreach (LuaVariableInfo fieldInfo in api.variables)
            {
                LuaApiVariable luaVariableDetails = fieldInfo.Attribute;

                snippet.Prefix = string.Format("{0}.{1}", luaApiDetails.luaName, luaVariableDetails.name);
                snippet.Body = snippet.Prefix;
                snippet.Description = luaVariableDetails.description;

                snippets.AppendLine(snippet.ConstructCSONString());
            }
        }

        foreach (LuaEnumInfo enumInfo in GetAllEnums())
        {
            foreach (LuaEnumValueInfo enumValueInfo in enumInfo.values)
            {
                if (enumValueInfo.Attribute != null && enumValueInfo.Attribute.hidden)
                {
                    continue;
                }
                
                snippet.Prefix = string.Format("{0}.{1}", enumInfo.Attribute.name, enumValueInfo.StringValue);
                snippet.Body = snippet.Prefix;
                snippet.Description = enumValueInfo.Attribute != null ? enumValueInfo.Attribute.description : string.Empty;
                
                snippet.DescriptionMoreUrl = string.Empty;
                
                snippets.AppendLine(snippet.ConstructCSONString());
            }
        }

        EditorGUIUtility.systemCopyBuffer = snippets.ToString();
    }
    
    /// <summary>
    /// Gets a clean function signature
    /// </summary>
    /// <returns>The clean function signature.</returns>
    /// <param name="method">Method.</param>
    /// <param name="functionAttrib">Function Attribute.</param>
    /// <param name="apiAttrib">API Attribute.</param>
    private static string GetCleanFunctionSignature(MethodInfo method, LuaApiFunction functionAttrib, LuaApi apiAttrib)
    {
        string apiName          = apiAttrib.luaName;
        string functionName     = functionAttrib.name;
        string paramsString     = string.Empty;

        foreach (ParameterInfo param in method.GetParameters())
        {
            paramsString += string.Format ("{0}, ", param.Name);
        }

        // Clean up last comma and space
        if (!string.IsNullOrEmpty (paramsString))
        {
            paramsString = paramsString.Remove (paramsString.Length - 2, 2);
        }

        return string.Format ("{0}.{1}({2})", apiName, functionName, paramsString);
    }

    /// <summary>
    /// Gets a documentation friendly name for a type.
    /// </summary>
    /// <returns>The clean type name.</returns>
    /// <param name="type">Type.</param>
    private static string GetCleanTypeName(Type type)
    {
        string result;

        // Ideally this would be a switch, but c# won't allow system.type in a switch
        if (type == typeof(void))
        {
            result = "Nothing";
        } 
        else if (type == typeof(float?))
        {
            result = "number (optional)";
        }
        else if (type == typeof(float[]))
        {
            result = "Table of numbers";
        } 
        else if (type == typeof(int)     ||
            type == typeof(uint)    ||
            type == typeof(float))
        {
            result = "number";
        }
        else if (type == typeof(string))
        {
            result = "string";
        }
        else if (type == typeof(bool))
        {
            result = "bool";
        }
        else if (type == typeof(MoonSharp.Interpreter.DynValue))
        {
            result = "Lua Type";
        }
        else if (type == typeof(MoonSharp.Interpreter.Table))
        {
            result = "Lua Table";
        }
        else if (type == typeof(bool?))
        {
            result = "bool (optional)";
        }
        else
        {
            Logger.Log (Channel.Lua, Priority.Error, "Failed to convert type {0} to cleaner name", type.ToString ());
            result = type.ToString ();
        }

        return result;
    }
    
}
#endif // UNITY_EDITOR