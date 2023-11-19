﻿using Microsoft.EntityFrameworkCore;
using Weasel.Audit.Extensions;
using Weasel.Audit.Interfaces;

namespace Weasel.Audit.Services;

public struct PostponedModelData<T, TEnum>
    where TEnum : struct, Enum
    where T : class
{
    public T Model { get; private set; }
    public TEnum ActionType { get; private set; }
    public object? Additional { get; private set; }
    public PostponedModelData(T model, TEnum actionType, object? additional = null)
    {
        Model = model;
        Additional = additional;
        ActionType = actionType;
    }
}

public interface IPosponedActionsStorage<TAuditAction, TEnum>
    where TAuditAction : class, IAuditAction<TEnum>
	where TEnum : struct, Enum
{
    IPostponedAuditManager<TAuditAction, TEnum> PostponedAuditManager { get; }
    public IAuditActionFactory<TAuditAction, TEnum> ActionFactory { get; }
    Task PlanPerformActionsAsync(DbContext context);
}
public sealed class PostponedAuditStorage<T, TAuditResult, TAuditAction, TEnum> : IPosponedActionsStorage<TAuditAction, TEnum>
    where T : class, IAuditable<TAuditResult, TAuditAction , TEnum>
    where TAuditResult : class, IAuditResult<TAuditAction, TEnum>
    where TAuditAction : class, IAuditAction<TEnum>
	where TEnum : struct, Enum
{
    private List<PostponedModelData<T, TEnum>> _postponedModels;

    public IPostponedAuditManager<TAuditAction, TEnum> PostponedAuditManager { get; private set; }
    public IAuditActionFactory<TAuditAction, TEnum> ActionFactory => PostponedAuditManager.ActionFactory;

    public PostponedAuditStorage(IPostponedAuditManager<TAuditAction, TEnum> postponedAuditManager)
    {
        PostponedAuditManager = postponedAuditManager;
        _postponedModels = new List<PostponedModelData<T, TEnum>>();
    }

    #region Postpone
    public void Postpone(T model, TEnum type, object? additional = null)
        => _postponedModels.Add(new PostponedModelData<T, TEnum>(model, type, additional));
    public void PostponeRange(IEnumerable<T> models, TEnum type, object? additional = null)
        => _postponedModels.AddRange(models.Select(x => new PostponedModelData<T, TEnum>(x, type, additional)));
    #endregion

    #region PlanPerformActions
    private async Task PlanAddActionsAsync(DbContext context, List<TAuditResult> list)
    {
        if (_postponedModels.Count == 0)
        {
            return;
        }

        foreach (var modelData in _postponedModels)
        {
            var model = modelData.Model;
            TAuditResult action = await model.AuditAsync(context);
            string entityId = context.GetAuditEntityId(model);
            action.Action = ActionFactory.CreateAuditAction(modelData.ActionType, entityId, modelData.Additional);
            list.Add(action);
        }
    }
    public async Task PlanPerformActionsAsync(DbContext context)
    {
        List<TAuditResult> actionAddList = new List<TAuditResult>();
        await PlanAddActionsAsync(context, actionAddList);
        await context.AddRangeAsync(actionAddList);
    }
    #endregion
}