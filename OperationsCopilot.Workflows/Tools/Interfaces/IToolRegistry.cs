namespace OperationsCopilot.Workflows.Tools.Interfaces;

public interface IToolRegistry<T> where T: class
{
    IReadOnlyCollection<T> Tools { get; }
    
    T? GetToolByName(string name);
}