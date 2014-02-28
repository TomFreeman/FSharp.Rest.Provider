using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using System.Web.Http;
using System.Web.Mvc;

public class DocumentationController : ApiController
{
    #region Public Methods and Operators

    public ConfigData Get()
    {
        IEnumerable<Type> types =
            Assembly.GetAssembly(typeof(DocumentationController))
                .GetTypes()
                .Where(x => x.IsClass && x.IsAssignableFrom(typeof(ApiController)) && x != typeof(DocumentationController));

        var parser = new ObjectParser();

        var d = new Dictionary<string, dynamic>();

        foreach (Type type in types)
        {
            d.Add(type.Name, parser.ParseObject(type));
        }

        IEnumerable<Type> etypes = Assembly.GetAssembly(typeof(DocumentationController)).GetTypes().Where(x => x.IsEnum);

        var e = new Dictionary<string, dynamic>();

        foreach (Type etype in etypes)
        {
            e.Add(etype.Name, parser.ParseEnum(etype));
        }

        IEnumerable<Type> controllers =
            Assembly.GetAssembly(typeof(DocumentationController))
                .GetTypes()
                .Where(x => typeof(ApiController).IsAssignableFrom(x) && x != typeof(DocumentationController));

        var m = new Dictionary<string, dynamic>();
        foreach (Type c in controllers)
        {
            m.Add("/api/" + c.Name.Replace("Controller", ""), parser.ParseController(c));
        }

        return new ConfigData { Classes = d, Enums = e, Methods = m };
    }

    #endregion

    public class ConfigData
    {
        #region Public Properties

        public dynamic Classes { get; set; }

        public dynamic Enums { get; set; }

        public dynamic Methods { get; set; }

        #endregion
    }
}

internal class ObjectParser
{
    #region Public Methods and Operators

    public dynamic ParseController(Type type)
    {
        var methods = new Dictionary<string, List<dynamic>>();
        string input = "none";
        bool requiresSSL = type.CustomAttributes.Any(x => x.AttributeType == typeof(RequireHttpsAttribute));
        bool unAuthenticated = type.CustomAttributes.All(x => x.AttributeType != typeof(PrincipalPermission));

        foreach (MethodInfo method in
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            ParameterInfo[] parms = method.GetParameters();
            if (parms.Any())
            {
                input = parms.First().ParameterType.Name;

                var path = parms.Select(p => new { Name = p.Name, Type = p.ParameterType.Name })
                                .Select(p => string.Format("{{{0}:{1}}}", p.Name, p.Type));

                var key = "/" + string.Join("/", path) + "/";

                AppendVerb(methods, key, method);
            }
            else
            {
                var key = "/";

                AppendVerb(methods, key, method);
            }
        }

        return
            new
                {
                    Methods = methods,
                    Input = input,
                    RequiresSSL = requiresSSL,
                    RequiresAuthentication = !unAuthenticated,
                };
    }

    private static void AppendVerb(Dictionary<string, List<dynamic>> methods, string key, MethodInfo method)
    {
        if (methods.ContainsKey(key))
        {
            methods[key].Add(method.Name);
        }
        else
        {
            methods.Add(key, new List<dynamic>() { new
                                                       {
                                                           method.Name,
                                                           Output = DetermineOutput(method.ReturnType)
                                                       }});
        }
    }

    private static dynamic DetermineOutput(Type outputType)
    {
        if (outputType.IsGenericType)
        {
            var generics = outputType.GenericTypeArguments.Select((t, i) => new {Index = i + 1, Name = t.Name});

            string originalName = outputType.Name;

            foreach (var generic in generics)
            {
                var oldString = string.Format("`{0}", generic.Index);
                originalName = originalName.Replace(oldString, string.Format("<{0}>", generic.Name));
            }

            return originalName;
        }

        return outputType.Name;
    }

    public dynamic ParseEnum(Type type)
    {
        var list = new List<string>();
        foreach (object val in Enum.GetValues(type))
        {
            list.Add(string.Format("{0} ({1})", Enum.Parse(type, val.ToString()), ((int)val)));
        }
        return list;
    }

    public dynamic ParseObject(Type type)
    {
        PropertyInfo[] props =
            type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty | BindingFlags.GetProperty)
                .ToArray();
        ;

        var dict = new Dictionary<string, dynamic>();
        foreach (PropertyInfo prop in props)
        {
            dict[prop.Name] = DetermineOutput(prop.PropertyType);
        }
        return dict;
    }

    #endregion
}