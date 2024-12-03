using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public interface IIfExpression_V1
{
    IReturnExpression_V1<T> Return<T>(T result);
}

public interface IIfExpression_V1<T>
{
    IReturnExpression_V1<T> Return(T result);
}

public interface IReturnableExpression_V1<T>
{
    IReturnExpression_V1<T> Return(T result);
}

public interface IReturnExpression_V1<T> : IElseableExpression_V1<T>, IElseIfableExpression_V1<T>
{

}

public interface IElseIfableExpression_V1<T>
{
    IReturnableExpression_V1<T> ElseIf(IExpression_V1<bool> condition);
}

public interface IElseableExpression_V1<T>
{
    IExpression_V1<T> Else(T result);
}

public static class Expression_V1
{
    public static IIfExpression_V1 If(IExpression_V1<bool> condition)
    {
        return new IfExpression_V1(condition);
    }

    class IfExpression_V1(IExpression_V1<bool> condition) : IIfExpression_V1
    {
        IReturnExpression_V1<T> IIfExpression_V1.Return<T>(T result)
        {
            return new ReturnExpression_V1<T>(new ConditionResult_V1<T>(condition, result));
        }

        class ConditionResult_V1<T>(IExpression_V1<bool> condition, T result) : IConditionResult_V1<T>
        {
            public event Action? ConditionChanged { add => mCondition.ResultChanged += value; remove => mCondition.ResultChanged -= value; }

            public bool GetResult([NotNullWhen(true)] out T? result)
            {
                result = mResult;
                return mCondition.Result;
            }

            readonly IExpression_V1<bool> mCondition = condition;
            readonly T mResult = result;
        }
    }

    class ReturnExpression_V1<T>(IConditionResult_V1<T> conditionResult) : IReturnExpression_V1<T>
    {
        public IExpression_V1<T> Else(T result)
        {
            return new ElseExpression_V1<T>(conditionResult, result);
        }

        public IReturnableExpression_V1<T> ElseIf(IExpression_V1<bool> condition)
        {
            return new ElseIfExpression<T>(conditionResult, condition);
        }
    }

    class ElseIfExpression<T>(IConditionResult_V1<T> conditionResult, IExpression_V1<bool> condition) : IReturnableExpression_V1<T>
    {
        public IReturnExpression_V1<T> Return(T result)
        {
            return new ReturnExpression_V1<T>(new ConditionResult_V1(conditionResult, condition, result));
        }

        class ConditionResult_V1 : IConditionResult_V1<T>
        {
            public event Action? ConditionChanged;

            public ConditionResult_V1(IConditionResult_V1<T> conditionResult, IExpression_V1<bool> condition, T result)
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

    class ElseExpression_V1<T> : IExpression_V1<T>
    {
        public event Action? ResultChanged;
        public T Result { get; private set; }

        internal ElseExpression_V1(IConditionResult_V1<T> conditionResult, T elseResult)
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

    interface IConditionResult_V1<T>
    {
        event Action? ConditionChanged;
        bool GetResult([NotNullWhen(true)] out T? result);
    }
}
