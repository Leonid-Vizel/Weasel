﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Weasel.Audit.Attributes;
using Weasel.Audit.Interfaces;

namespace Weasel.Audit.Extensions;

public struct IncludeAllCacheKey
{
    public Type Type { get; private set; }
    public int Depth { get; private set; }
    public IncludeAllCacheKey(Type type, int depth)
    {
        Type = type;
        Depth = depth;
    }
}

public static class DbContextExtensions
{
    private static ConcurrentDictionary<IncludeAllCacheKey, List<string>> _actionIncludePaths
        = new ConcurrentDictionary<IncludeAllCacheKey, List<string>>();
    private static ConcurrentDictionary<Type, IReadOnlyList<IProperty>> _typePrimaryKeys
        = new ConcurrentDictionary<Type, IReadOnlyList<IProperty>>();
    private static readonly MethodInfo setMethod
        = typeof(DbContext).GetMethods().Single(p => p.Name == nameof(DbContext.Set) && p.ContainsGenericParameters && p.GetParameters().Length == 0);

    public static IEnumerable<string> GetIncludePaths<T>(this DbContext context, int depth = 20)
        => context.GetIncludePaths(typeof(T), depth);
    public static IEnumerable<string> GetIncludePaths(this DbContext context, Type type, int depth = 20)
    {
        var key = new IncludeAllCacheKey(type, depth);
        if (!_actionIncludePaths.TryGetValue(key, out var list))
        {
            list = context.CalculateIncludePaths(type, depth).ToList();
            _actionIncludePaths.TryAdd(key, list);
        }
        return list;
    }
    private static IEnumerable<string> CalculateIncludePaths(this DbContext context, Type clrEntityType, int depth = 20)
    {
        var entityType = context.Model.FindEntityType(clrEntityType);
        if (entityType == null)
        {
            yield break;
        }
        var stack = new Stack<IEnumerator<INavigation>>();
        bool flag = true;
        while (flag)
        {
            var entityNavigations = new List<INavigation>();
            if (stack.Count <= depth)
            {
                foreach (var navigation in entityType.GetNavigations())
                {
                    if (navigation.PropertyInfo?.GetCustomAttribute<PreventCycleAttribute>() == null)
                    {
                        entityNavigations.Add(navigation);
                    }
                }
            }
            if (entityNavigations.Count == 0)
            {
                if (stack.Count > 0)
                {
                    yield return string.Join(".", stack.Reverse().Select(e => e.Current.Name));
                }
            }
            else
            {
                stack.Push(entityNavigations.GetEnumerator());
            }
            while (stack.Count > 0 && !stack.Peek().MoveNext())
            {
                stack.Pop();
            }
            if (stack.Count == 0)
            {
                flag = false;
            }
            else
            {
                entityType = stack.Peek().Current.TargetEntityType;
            }
        }
    }
    public static IQueryable<IAuditResult<TAuditAction>>? GetAuditResultQueryable<TAuditAction>(this DbContext context, Type type)
        where TAuditAction : class, IAuditAction
        => setMethod.MakeGenericMethod(type).Invoke(context, null) as IQueryable<IAuditResult<TAuditAction>>;
    public static IQueryable<IAuditResult<TAuditAction>> IncludeAuditResult<TAuditAction>(this DbContext context, Type type, int depth = 20)
        where TAuditAction : class, IAuditAction
    {
        var query = context.GetAuditResultQueryable<TAuditAction>(type);
        if (query == null)
        {
            throw new Exception($"Cant get set of {type.FullName} from passed DbContext instance");
        }
        var paths = context.GetIncludePaths(type, depth);
        foreach (var path in paths)
        {
            query = query.Include(path);
        }
        return query;
    }
    public static string GetAuditEntityId(this DbContext context, object model)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }
        var entry = context.Entry(model);
        var properties = context.FindEntityPrimaryKeys(model);
        string?[] keys = new string[properties.Count];
        for (int i = 0; i < properties.Count; i++)
        {
            keys[i] = entry.Property(properties[i]).CurrentValue?.ToString();
        }
        return JsonSerializer.Serialize(keys);
    }
    public static IReadOnlyList<IProperty> FindEntityPrimaryKeys(this DbContext context, object model)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }
        Type type = model.GetType();
        if (_typePrimaryKeys.TryGetValue(type, out var list))
        {
            return list;
        }
        var entityType = context.Model.FindEntityType(type);
        if (entityType == null)
        {
            throw new Exception($"EF EntityType for '{type.FullName}' not found!");
        }
        var primaryKey = entityType.FindPrimaryKey();
        if (primaryKey == null)
        {
            throw new Exception($"EF primary key for '{type.FullName}' not found!");
        }
        return primaryKey.Properties;
    }
}
