﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weasel.Audit.Interfaces;
using Weasel.Audit.Services;

namespace Weasel.Audit.DependencyInjection.Extensions;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAudit<TDbContext, TFactoryImplementation, TAuditAction, TEnum>(this IServiceCollection services)
        where TFactoryImplementation : class, IAuditActionFactory<TAuditAction, TEnum>
        where TAuditAction : class, IAuditAction<TEnum>
        where TDbContext : DbContext
		where TEnum : struct, Enum
	{
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }
        services.AddSingleton<IAuditPropertyStorage, AuditPropertyStorage>();
        services.AddSingleton<IAuditActionFactory<TAuditAction, TEnum>, TFactoryImplementation>();
        var manager = new AuditSchemeManager<TEnum>();
        services.AddSingleton<IAuditSchemeManager<TEnum>, AuditSchemeManager<TEnum>>((x) => manager);
        services.AddScoped<IAuditPropertyManager, AuditPropertyManager>();
        services.AddScoped<IPostponedAuditManager<TAuditAction, TEnum>, PostponedAuditManager<TDbContext, TAuditAction, TEnum>>();
        return services;
    }
}
