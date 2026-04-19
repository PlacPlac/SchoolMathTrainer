namespace SharedCore.Models;

public sealed class MathProblem
{
    public int LeftOperand { get; init; }
    public int RightOperand { get; init; }
    public int CorrectAnswer { get; init; }
    public OperationType OperationType { get; init; }
    public IReadOnlyList<int> Options { get; init; } = Array.Empty<int>();

    public string Expression =>
        OperationType == OperationType.Addition
            ? $"{LeftOperand} + {RightOperand}"
            : $"{LeftOperand} - {RightOperand}";
}
