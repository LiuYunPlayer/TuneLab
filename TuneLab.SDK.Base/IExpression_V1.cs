namespace TuneLab.SDK.Base;

public interface IExpression_V1<out T>
{
    event Action? ResultChanged;
    T Result { get; }
}

public static class IExpression_V1Extensions
{
    public static IExpression_V1<bool> And(this IExpression_V1<bool> left, IExpression_V1<bool> right)
    {
        return new AndExpression_V1(left, right);
    }

    class AndExpression_V1 : IExpression_V1<bool>
    {
        public event Action? ResultChanged;
        public bool Result { get; private set; }

        public AndExpression_V1(IExpression_V1<bool> left, IExpression_V1<bool> right)
        {
            mLeft = left;
            mRight = right;
            Result = Test();
            mLeft.ResultChanged += OnResultChanged;
            mRight.ResultChanged += OnResultChanged;
        }

        bool Test()
        {
            return mLeft.Result && mRight.Result;
        }

        void OnResultChanged()
        {
            var result = Test();
            if (Result != result)
            {
                Result = result;
                ResultChanged?.Invoke();
            }
        }

        readonly IExpression_V1<bool> mLeft;
        readonly IExpression_V1<bool> mRight;
    }

    public static IExpression_V1<bool> Or(this IExpression_V1<bool> left, IExpression_V1<bool> right)
    {
        return new OrExpression_V1(left, right);
    }

    class OrExpression_V1 : IExpression_V1<bool>
    {
        public event Action? ResultChanged;
        public bool Result { get; private set; }

        public OrExpression_V1(IExpression_V1<bool> left, IExpression_V1<bool> right)
        {
            mLeft = left;
            mRight = right;
            Result = Test();
            mLeft.ResultChanged += OnResultChanged;
            mRight.ResultChanged += OnResultChanged;
        }

        bool Test()
        {
            return mLeft.Result || mRight.Result;
        }

        void OnResultChanged()
        {
            var result = Test();
            if (Result != result)
            {
                Result = result;
                ResultChanged?.Invoke();
            }
        }

        readonly IExpression_V1<bool> mLeft;
        readonly IExpression_V1<bool> mRight;
    }

    public static IExpression_V1<bool> Not(this IExpression_V1<bool> expression)
    {
        return new NotExpression_V1(expression);
    }

    class NotExpression_V1 : IExpression_V1<bool>
    {
        public event Action? ResultChanged;
        public bool Result => !mExpression.Result;

        public NotExpression_V1(IExpression_V1<bool> expression)
        {
            mExpression = expression;

            mExpression.ResultChanged += ResultChanged;
        }

        readonly IExpression_V1<bool> mExpression;
    }

    public static IExpression_V1<TOut> Excute<TIn, TOut>(this IExpression_V1<TIn> expression, Func<TIn, TOut> function)
    {
        return new ExcuteExpression_V1<TIn, TOut>(expression, function);
    }

    class ExcuteExpression_V1<TIn, TOut> : IExpression_V1<TOut>
    {
        public event Action? ResultChanged;
        public TOut Result => mFunction(mExpression.Result);

        public ExcuteExpression_V1(IExpression_V1<TIn> expression, Func<TIn, TOut> function)
        {
            mExpression = expression;
            mFunction = function;

            mExpression.ResultChanged += ResultChanged;
        }

        readonly IExpression_V1<TIn> mExpression;
        readonly Func<TIn, TOut> mFunction;
    }

    public static IExpression_V1<T> ToExpression<T>(this T result)
    {
        return new ConstantExpression_V1<T>(result);
    }

    class ConstantExpression_V1<T>(T result) : IExpression_V1<T>
    {
        public event Action? ResultChanged { add { } remove { } }
        public T Result => result;
    }
}
