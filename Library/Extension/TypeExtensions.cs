namespace HackyMessage.Extension;

public static class TypeExtensions
{
    public static string GetFriendlyName(this Type type)
    {
        if (!type.IsGenericType)
            return type.Name; // Use .FullName if you want namespaces included

        // Strip the backtick and number (e.g., "Foo`1" becomes "Foo")
        string typeName = type.Name;
        int backtickIndex = typeName.IndexOf('`');
        if (backtickIndex > 0)
        {
            typeName = typeName.Substring(0, backtickIndex);
        }

        // Recursively get friendly names for all generic arguments
        var genericArgs = type.GetGenericArguments().Select(t => t.GetFriendlyName());
        
        return $"{typeName}<{string.Join(", ", genericArgs)}>";
    }
}