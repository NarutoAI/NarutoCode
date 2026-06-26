using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Infrastructure.Tests;

public class NullWorkspaceContextAccessor:IWorkspaceContextAccessor
{
    public WorkspaceContext Current { get; }
}