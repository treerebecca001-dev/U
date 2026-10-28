namespace SceneWorkbench.Core;

public interface IEditCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class CommandHistory
{
    private readonly Stack<IEditCommand> undo = new();
    private readonly Stack<IEditCommand> redo = new();
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        undo.Push(command);
        redo.Clear();
    }

    public void Undo()
    {
        if (!undo.TryPeek(out var command))
            return;
        command.Undo();
        undo.Pop();
        redo.Push(command);
    }

    public void Redo()
    {
        if (!redo.TryPeek(out var command))
            return;
        command.Execute();
        redo.Pop();
        undo.Push(command);
    }
}
