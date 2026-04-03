using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HDTplugins.Services
{
    internal static class EmbeddedJsonLoader
    {
        public static string ReadRequiredText(string resourceName)
        {
            var assembly = typeof(EmbeddedJsonLoader).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded resource not found.", resourceName);

                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }
    }
}
