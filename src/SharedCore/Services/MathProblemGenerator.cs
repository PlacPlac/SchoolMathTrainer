using SharedCore.Models;

namespace SharedCore.Services;

public sealed class MathProblemGenerator
{
    private readonly Random _random = new();

    public MathProblem CreateBeginnerProblem(int maxValue = 20, int optionCount = 4)
    {
        var problem = CreateProblem(maxValue);
        var options = new HashSet<int> { problem.CorrectAnswer };

        while (options.Count < optionCount)
        {
            var spread = Math.Max(3, Math.Min(6, maxValue / 3));
            var candidate = Math.Clamp(problem.CorrectAnswer + _random.Next(-spread, spread + 1), 0, maxValue);
            if (candidate != problem.CorrectAnswer)
            {
                options.Add(candidate);
            }
        }

        return new MathProblem
        {
            LeftOperand = problem.LeftOperand,
            RightOperand = problem.RightOperand,
            CorrectAnswer = problem.CorrectAnswer,
            OperationType = problem.OperationType,
            Options = options.OrderBy(_ => _random.Next()).ToArray()
        };
    }

    public MathProblem CreateAdvancedProblem(int maxValue = 20)
    {
        return CreateProblem(maxValue);
    }

    public IReadOnlyList<int> CreateAdvancedAnswerOptions(int correctAnswer, int maxValue = 20, int optionCount = 10)
    {
        var clampedOptionCount = Math.Clamp(optionCount, 1, maxValue + 1);
        var candidatePool = Enumerable.Range(0, maxValue + 1)
            .Where(value => value != correctAnswer)
            .OrderBy(value => Math.Abs(value - correctAnswer))
            .ThenBy(_ => _random.Next())
            .Take(clampedOptionCount - 1);

        return candidatePool
            .Append(correctAnswer)
            .OrderBy(_ => _random.Next())
            .ToArray();
    }

    private MathProblem CreateProblem(int maxValue)
    {
        var useAddition = _random.Next(0, 2) == 0;
        if (useAddition)
        {
            var left = _random.Next(0, maxValue + 1);
            var right = _random.Next(0, maxValue - left + 1);
            return new MathProblem
            {
                LeftOperand = left,
                RightOperand = right,
                CorrectAnswer = left + right,
                OperationType = OperationType.Addition
            };
        }

        var minuend = _random.Next(0, maxValue + 1);
        var subtrahend = _random.Next(0, minuend + 1);
        return new MathProblem
        {
            LeftOperand = minuend,
            RightOperand = subtrahend,
            CorrectAnswer = minuend - subtrahend,
            OperationType = OperationType.Subtraction
        };
    }
}
