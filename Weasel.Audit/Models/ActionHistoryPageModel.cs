﻿using Weasel.Audit.Interfaces;

namespace Weasel.Audit.Models;

public sealed class ActionHistoryPageModel
{
    public IAuditAction Action { get; set; } = null!;
    public ActionIndexItem[] Items { get; set; } = null!;
}