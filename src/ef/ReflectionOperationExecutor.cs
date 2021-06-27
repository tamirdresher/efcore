// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Tools.Properties;
using Microsoft.Extensions.DependencyInjection;
using ReflectionMagic;

namespace Microsoft.EntityFrameworkCore.Tools
{
    internal class ReflectionOperationExecutor : OperationExecutorBase
    {
        private readonly object _executor = null;
        private readonly Assembly _commandsAssembly = null;
        private const string ReportHandlerTypeName = "Microsoft.EntityFrameworkCore.Design.OperationReportHandler";
        private const string ResultHandlerTypeName = "Microsoft.EntityFrameworkCore.Design.OperationResultHandler";
        private readonly Type _resultHandlerType = null;
        private readonly AppServiceProviderFactory _appServicesFactory;

        public ReflectionOperationExecutor(
            string assembly,
            string startupAssembly,
            string projectDir,
            string dataDirectory,
            string rootNamespace,
            string language,
            string[] remainingArguments)
            : base(assembly, startupAssembly, projectDir, rootNamespace, language, remainingArguments)
        {
            if (dataDirectory != null)
            {
                Reporter.WriteVerbose(Resources.UsingDataDir(dataDirectory));
                AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory);
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;


            //_commandsAssembly = Assembly.Load(new AssemblyName { Name = DesignAssemblyName });
            //var reportHandlerType = _commandsAssembly.GetType(ReportHandlerTypeName, throwOnError: true, ignoreCase: false);

            //var reportHandler = Activator.CreateInstance(
            //    reportHandlerType,
            //    (Action<string>)Reporter.WriteError,
            //    (Action<string>)Reporter.WriteWarning,
            //    (Action<string>)Reporter.WriteInformation,
            //    (Action<string>)Reporter.WriteVerbose);

            //_executor = Activator.CreateInstance(
            //    _commandsAssembly.GetType(ExecutorTypeName, throwOnError: true, ignoreCase: false),
            //    reportHandler,
            //    new Dictionary<string, object>
            //    {
            //        { "targetName", AssemblyFileName },
            //        { "startupTargetName", StartupAssemblyFileName },
            //        { "projectDir", ProjectDirectory },
            //        { "rootNamespace", RootNamespace },
            //        { "language", Language },
            //        { "toolsVersion", ProductInfo.GetVersion() },
            //        { "remainingArguments", RemainingArguments }
            //    });

            //_resultHandlerType = _commandsAssembly.GetType(ResultHandlerTypeName, throwOnError: true, ignoreCase: false);
            var startupAssemblyObj = Assembly.Load(StartupAssemblyFileName);
            var contextAssemblyObj = Assembly.Load(AssemblyFileName);
            var efCoreAssemblyObj = Assembly.Load("Microsoft.EntityFrameworkCore");
            var efCoreRelationalAssembly = Assembly.Load("Microsoft.EntityFrameworkCore.Relational");
            _appServicesFactory = new AppServiceProviderFactory(startupAssemblyObj);
            var ctxts = FindContextTypes(startupAssemblyObj, contextAssemblyObj, efCoreAssemblyObj);


            foreach (var ctxtFactory in ctxts)
            {
                Console.WriteLine($"Creating {ctxtFactory.Key}");
                var ctxt = ctxtFactory.Value();
                dynamic ctxtd = ctxt;
                Console.WriteLine(ctxtd.Model.DebugView.View);

                var metadataExtensions = new MetadataExtensions(efCoreRelationalAssembly);

                foreach (var entityType in ctxtd.Model.GetEntityTypes())
                {
                    try
                    {
                        var tableName = metadataExtensions.GetTableName(entityType);
                        var schemaName = metadataExtensions.GetSchema(entityType);
                        Console.WriteLine($"Entity {entityType.ClrType} TableName: {tableName} Schema {schemaName}");
                        foreach (var propertyType in entityType.GetProperties())
                        {
                            var columnName = metadataExtensions.GetColumnName( propertyType);
                            Console.WriteLine($"\t\t Property {propertyType.Name} TableName: {columnName}");

                        }
                    }
                    catch (Exception)
                    {

                        throw;
                    }

                }
            }
            Console.WriteLine($"Done here {ctxts.Count}");
            Console.ReadKey();
        }
        private IDictionary<Type, Func<object>> FindContextTypes(Assembly startupAssembly, Assembly contextAssembly, Assembly efCoreAssembly)
        {

            var contexts = new Dictionary<Type, Func<object>>();


            //Look for DbContext classes registered in the service provider

            var appServices = _appServicesFactory.Create(RemainingArguments);
            var dbContextOptionsType = efCoreAssembly.GetType("Microsoft.EntityFrameworkCore.DbContextOptions");
            var registeredContexts = appServices.GetServices(dbContextOptionsType)
                .Select(o => (Type)((dynamic)o).ContextType);
            foreach (var context in registeredContexts.Where(c => !contexts.ContainsKey(c)))
            {
                contexts.Add(
                    context,
                    FindContextFactory(context, efCoreAssembly)
                    ?? FindContextFromRuntimeDbContextFactory(appServices, context, efCoreAssembly)
                    ?? (() => ActivatorUtilities.GetServiceOrCreateInstance(appServices, context)));
            }
            var provider = appServices;

            // Look for DbContext classes in assemblies
            var types = GetConstructibleTypes(startupAssembly)
                .Concat(GetConstructibleTypes(contextAssembly))
                .ToList();

            var dbContextBaseType = efCoreAssembly.GetType("Microsoft.EntityFrameworkCore.DbContext");

            var contextTypes = types.Where(t => dbContextBaseType.IsAssignableFrom(t)).Select(
                    t => t.AsType())
                .Distinct();

            foreach (var context in contextTypes.Where(c => !contexts.ContainsKey(c)))
            {
                contexts.Add(
                    context,
                    () =>
                    {
                        try
                        {
                            try
                            {
                                return ActivatorUtilities.GetServiceOrCreateInstance(provider, context);
                            }
                            catch
                            {
                                var ctor = context.GetConstructors().First(ctor =>
                                {
                                    var ctorParams = ctor.GetParameters();
                                    return ctorParams.Count() == 1 && ctorParams.First().ParameterType == typeof(string);
                                });
                                return ctor.Invoke(new[] { "data source=(localdb)\\mssqllocaldb;initial catalog=dummy;integrated security=True;MultipleActiveResultSets=True;MultiSubnetFailover=True;App=dummy" });
                            }
                        }
                        catch (Exception ex)
                        {


                            throw new Exception($"no parameterless ctor {context.Name}", ex);
                        }
                    });
            }

            return contexts;
        }

        IEnumerable<TypeInfo> GetConstructibleTypes(Assembly assembly)
            => GetLoadableDefinedTypes(assembly).Where(
                t => !t.IsAbstract
                    && !t.IsGenericTypeDefinition);

        public static IEnumerable<TypeInfo> GetLoadableDefinedTypes(Assembly assembly)
        {
            try
            {
                return assembly.DefinedTypes;
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Select(IntrospectionExtensions.GetTypeInfo);
            }
        }

        private Func<object> FindContextFactory(Type contextType, Assembly efCoreAssembly)
        {
            var iDesignTimeDbContextFactoryType = efCoreAssembly.GetTypes().First(t => t.Name.Contains(".IDesignTimeDbContextFactory"));
            var factoryInterface = iDesignTimeDbContextFactoryType.MakeGenericType(contextType);
            var factory = GetConstructibleTypes(contextType.Assembly)
                .FirstOrDefault(t => factoryInterface.IsAssignableFrom(t));
            return factory == null ? (Func<object>)null : (() => CreateContextFromFactory(factory.AsType(), contextType, iDesignTimeDbContextFactoryType));
        }

        private Func<object> FindContextFromRuntimeDbContextFactory(IServiceProvider appServices, Type contextType, Assembly efCoreAssembly)
        {
            var iDesignTimeDbContextFactoryType = efCoreAssembly.GetTypes().First(t => t.Name.Contains(".IDesignTimeDbContextFactory"));
            var factoryInterface = iDesignTimeDbContextFactoryType.MakeGenericType(contextType);
            var service = appServices.GetService(factoryInterface);
            return service == null
                ? (Func<object>)null
                : () => (object)factoryInterface.GetRuntimeMethods().First(mtd => mtd.Name.Contains("CreateDbContext"))
                    ?.Invoke(service, null);
        }

        private object CreateContextFromFactory(Type factory, Type contextType, Type iDesignTimeDbContextFactoryType)
        {

            return (object)iDesignTimeDbContextFactoryType.MakeGenericType(contextType)
                .GetMethod("CreateDbContext", new[] { typeof(string[]) })
                .Invoke(Activator.CreateInstance(factory), new object[] { RemainingArguments });
        }

        protected override object CreateResultHandler()
            => Activator.CreateInstance(_resultHandlerType);

        protected override void Execute(string operationName, object resultHandler, IDictionary arguments)
            => Activator.CreateInstance(
                _commandsAssembly.GetType(ExecutorTypeName + "+" + operationName, throwOnError: true, ignoreCase: true),
                _executor,
                resultHandler,
                arguments);

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            Console.WriteLine("ResolveAssembly");
            var assemblyName = new AssemblyName(args.Name);

            var basePaths = new List<string>()
            {
                AppBasePath
            };
            //if (assemblyName.Name.Contains(DesignAssemblyName))
            //{
            //    basePaths.Insert(0,@"C:\Users\tamirdr\source\repos\tamirdresher\efcore\artifacts\bin\EFCore.Design\Debug\netstandard2.1");
            //}
            foreach (var basePath in basePaths)
            {
                foreach (var extension in new[] { ".dll", ".exe" })
                {
                    var path = Path.Combine(basePath, assemblyName.Name + extension);
                    if (File.Exists(path))
                    {
                        try
                        {
                            return Assembly.LoadFrom(path);
                        }
                        catch
                        {
                        }
                    }
                }

            }

            return null;
        }

        public override void Dispose()
            => AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
    }

    /// <summary>
    /// Based on Cédric Luthi (0xced) code from https://github.com/dotnet/efcore/issues/18256
    /// </summary>
    class MetadataExtensions
    {
        private readonly Assembly EfCoreRelationalAssembly;
        // EF Core 2
        private dynamic RelationalMetadataExtensions => EfCoreRelationalAssembly?.GetType("Microsoft.EntityFrameworkCore.RelationalMetadataExtensions")?.AsDynamicType();
        // EF Core 3
        private dynamic RelationalEntityTypeExtensions => EfCoreRelationalAssembly?.GetType("Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions")?.AsDynamicType();
        private dynamic RelationalPropertyExtensions => EfCoreRelationalAssembly?.GetType("Microsoft.EntityFrameworkCore.RelationalPropertyExtensions")?.AsDynamicType();

        public MetadataExtensions(Assembly efCoreRelationalAssembly)
        {
            EfCoreRelationalAssembly = efCoreRelationalAssembly;
        }
        public string GetSchema(dynamic entityType)
        {
            if (RelationalEntityTypeExtensions != null)
                return RelationalEntityTypeExtensions.GetSchema(entityType);
            if (RelationalMetadataExtensions != null)
                return RelationalMetadataExtensions.Relational(entityType).Schema;
            throw NotSupportedException();
        }

        public string GetTableName(dynamic entityType)
        {
            if (RelationalEntityTypeExtensions != null)
                return RelationalEntityTypeExtensions.GetTableName(entityType);
            if (RelationalMetadataExtensions != null)
                return RelationalMetadataExtensions.Relational(entityType).TableName;
            throw NotSupportedException();
        }

        public string GetColumnName(dynamic property)
        {
            if (RelationalPropertyExtensions != null)
                return RelationalPropertyExtensions.GetColumnName(property);
            if (RelationalMetadataExtensions != null)
                return RelationalMetadataExtensions.Relational(property).ColumnName;
            throw NotSupportedException();
        }

        private Exception NotSupportedException()
        {
            if (EfCoreRelationalAssembly == null)
                throw new InvalidOperationException($"The 'Microsoft.EntityFrameworkCore.Relational' assembly was not found as a referenced assembly.");
            return new NotSupportedException($"Found neither 'Microsoft.EntityFrameworkCore.RelationalMetadataExtensions' (expected in EF Core 2) nor 'Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions' (expected in EF Core 3). Did Microsoft introduce a breaking change in {EfCoreRelationalAssembly.GetName()} ?");
        }
    }
}
