// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.EntityFrameworkCore.Tools.Commands;

namespace Microsoft.EntityFrameworkCore.Tools
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.WriteLine("WaitingForDebuggerToAttach");
            Console.WriteLine($"ProcessId {Process.GetCurrentProcess().Id}");
            Console.ReadLine();

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;


            if (Console.IsOutputRedirected)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }

            var app = new CommandLineApplication { Name = "ef" };

            new RootCommand().Configure(app);

            try
            {
                return app.Execute(args);
            }
            catch (Exception ex)
            {
                var wrappedException = ex as WrappedException;
                if (ex is CommandException
                    || ex is CommandParsingException
                    || (wrappedException?.Type == "Microsoft.EntityFrameworkCore.Design.OperationException"))
                {
                    Reporter.WriteVerbose(ex.ToString());
                }
                else
                {
                    Reporter.WriteInformation(ex.ToString());
                }

                Reporter.WriteError(ex.Message);

                return 1;
            }
        }

        private static System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var asmPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), new AssemblyName(args.Name).Name + ".dll");
            Console.WriteLine($"AssemblyResolve {args.Name}  {asmPath} {args.RequestingAssembly}");
            return Assembly.LoadFrom(asmPath);
        }
    }
}
