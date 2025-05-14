using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Utils;

public static class AssemblyHelper
{
    public static IDisposable RegisterAssemblyResolve(string directory)
    {
        return new AssemblyResolveRegistration(directory);
    }

    class AssemblyResolveRegistration : IDisposable
    {
        public AssemblyResolveRegistration(string directory)
        {
            handler = (sender, args) =>
            {
                string assemblyPath = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
                return null;
            };

            AppDomain.CurrentDomain.AssemblyResolve += handler;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= handler;
        }

        ResolveEventHandler handler;
    }
}
