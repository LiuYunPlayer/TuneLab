using System.Diagnostics.CodeAnalysis;

namespace TuneLab.Foundation;

public interface IIfExpression
{
    IReturnExpression<T> Return<T>(T result);
}

public interface IIfExpression<T>
{
    IReturnExpression<T> Return(T result);
}

public interface IReturnableExpression<T>
{
    IReturnExpression<T> Return(T result);
}

public interface IReturnExpression<T> : IElseableExpression<T>, IElseIfableExpression<T>
{

}

public interface IElseIfableExpression<T>
{
    IReturnableExpression<T> ElseIf(IExpression<bool> condition);
}

public interface IElseableExpression<T>
{
    IExpression<T> Else(T result);
}

public static class Expression
{
    public static IIfExpression If(IExpression<bool> condition)
    {
        return new IfExpression(condition);
    }

    class IfExpression(IExpression<bool> condition) : IIfExpression
    {
        IReturnExpression<T> IIfExpression.Return<T>(T result)
        {
            return new ReturnExpression<T>(new ConditionResult<T>(condition, result));
        }

        class ConditionResult<T>(IExpression<bool> condition, T result) : IConditionResult<T>
        {
            public event Action? ConditionChanged { add => mCondition.ResultChanged += value; remove => mCondition.ResultChanged -= value; }

            public bool GetResult([NotNullWhen(true)] out T? result)
            {
                result = mResult;
                return mCondition.Result;
            }

            readonly IExpression<bool> mCondition = condition;
            readonly T mResult = result;
        }
    }

    class ReturnExpression<T>(IConditionResult<T> conditionResult) : IReturnExpression<T>
    {
        public IExpression<T> Else(T result)
        {
            return new ElseExpression<T>(conditionResult, result);
        }

        public IReturnableExpression<T> ElseIf(IExpression<bool> condition)
        {
            return new ElseIfExpression<T>(conditionResult, condition);
        }
    }

    class ElseIfExpression<T>(IConditionResult<T> conditionResult, IExpression<bool> condition) : IReturnableExpression<T>
    {
        public IReturnExpression<T> Return(T result)
        {
            return new ReturnExpression<T>(new ConditionResult(conditionResult, condition, result));
        }

        class ConditionResult : IConditionResult<T>
        {
            public event Action? ConditionChanged;

            public ConditionResult(IConditionResult<T> conditionResult, IExpression<bool> condition, T result)
            {
                conditionResult.ConditionChanged += () =>
                {
                    mTempCondition = conditionResult.GetResult(out mTempResult);
                    mLastConditionResultCondition = mTempCondition;
                    if (mLastConditionResultCondition)
                        return;

                    mTempCondition = condition.Result;
                    mTempResult = mTempCondition ? result : default;

                    ConditionChanged?.Invoke();
                };

                condition.ResultChanged += () =>
                {
                    if (mLastConditionResultCondition)
                        return;

                    mTempCondition = condition.Result;
                    mTempResult = mTempCondition ? result : default;
                };

                mTempCondition = conditionResult.GetResult(out mTempResult);
                mLastConditionResultCondition = mTempCondition;
                if (mLastConditionResultCondition)
                    return;

                mTempCondition = condition.Result;
                mTempResult = mTempCondition ? result : default;
            }

            public bool GetResult([NotNullWhen(true)] out T? result)
            {
                result = mTempResult;
                return mTempCondition;
            }

            bool mLastConditionResultCondition;

            bool mTempCondition;
            T? mTempResult;
        }
    }

    class ElseExpression<T> : IExpression<T>
    {
        public event Action? ResultChanged;
        public T Result { get; private set; }

        internal ElseExpression(IConditionResult<T> conditionResult, T elseResult)
        {
            mElseResult = elseResult;

            conditionResult.ConditionChanged += () =>
            {
                Result = conditionResult.GetResult(out var result) ? result : mElseResult;
                ResultChanged?.Invoke();
            };

            Result = conditionResult.GetResult(out var result) ? result : mElseResult;
        }

        readonly T mElseResult;
    }

    interface IConditionResult<T>
    {
        event Action? ConditionChanged;
        bool GetResult([NotNullWhen(true)] out T? result);
    }
}
