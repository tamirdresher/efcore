// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.EntityFrameworkCore.Tools
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class AppServiceProviderFactory
    {
        private readonly Assembly _startupAssembly;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public AppServiceProviderFactory([NotNull] Assembly startupAssembly)
        {
            _startupAssembly = startupAssembly;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IServiceProvider Create([NotNull] string[] args)
        {

            return CreateFromHosting(args)
                ?? CreateEmptyServiceProvider();
        }

        private IServiceProvider CreateFromHosting(string[] args)
        {

            var serviceProviderFactory = HostFactoryResolver.ResolveServiceProviderFactory(_startupAssembly);
            if (serviceProviderFactory == null)
            {

                return null;
            }

            var aspnetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            var environment = aspnetCoreEnvironment
                ?? dotnetEnvironment
                ?? "Development";
            if (aspnetCoreEnvironment == null)
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
            }

            if (dotnetEnvironment == null)
            {
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
            }


            try
            {
                var services = serviceProviderFactory(args);
                if (services == null)
                {

                    return null;
                }


                return services.CreateScope().ServiceProvider;
            }
            catch (Exception ex)
            {
                if (ex is TargetInvocationException)
                {
                    ex = ex.InnerException;
                }


                return null;
            }
        }

        private IServiceProvider CreateEmptyServiceProvider()
        {

            return new ServiceCollection().BuildServiceProvider();
        }
    }
}
