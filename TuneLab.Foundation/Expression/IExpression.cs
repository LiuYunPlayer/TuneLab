namespace TuneLab.Foundation;

public interface IExpression<out T>
{
    event Action? ResultChanged;
    T Result { get; }
}

public static class IExpressionExtensions
{
    public static IExpression<bool> And(this IExpression<bool> left, IExpression<bool> right)
    {
        return new AndExpression(left, right);
    }

    class AndExpression : IExpression<bool>
    {
        public event Action? ResultChanged;
        public bool Result { get; private set; }

        public AndExpression(IExpression<bool> left, IExpression<bool> right)
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

        readonly IExpression<bool> mLeft;
        readonly IExpression<bool> mRight;
    }

    public static IExpression<bool> Or(this IExpression<bool> left, IExpression<bool> right)
    {
        return new OrExpression(left, right);
    }

    class OrExpression : IExpression<bool>
    {
        public event Action? ResultChanged;
        public bool Result { get; private set; }

        public OrExpression(IExpression<bool> left, IExpression<bool> right)
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

        readonly IExpression<bool> mLeft;
        readonly IExpression<bool> mRight;
    }

    public static IExpression<bool> Not(this IExpression<bool> expression)
    {
        return new NotExpression(expression);
    }

    class NotExpression : IExpression<bool>
    {
        public event Action? ResultChanged;
        public bool Result => !mExpression.Result;

        public NotExpression(IExpression<bool> expression)
        {
            mExpression = expression;

            mExpression.ResultChanged += ResultChanged;
        }

        readonly IExpression<bool> mExpression;
    }

    public static IExpression<TOut> Excute<TIn, TOut>(this IExpression<TIn> expression, Func<TIn, TOut> function)
    {
        return new ExcuteExpression<TIn, TOut>(expression, function);
    }

    class ExcuteExpression<TIn, TOut> : IExpression<TOut>
    {
        public event Action? ResultChanged;
        public TOut Result => mFunction(mExpression.Result);

        public ExcuteExpression(IExpression<TIn> expression, Func<TIn, TOut> function)
        {
            mExpression = expression;
            mFunction = function;

            mExpression.ResultChanged += ResultChanged;
        }

        readonly IExpression<TIn> mExpression;
        readonly Func<TIn, TOut> mFunction;
    }

    public static IExpression<T> ToExpression<T>(this T result)
    {
        return new ConstantExpression<T>(result);
    }

    class ConstantExpression<T>(T result) : IExpression<T>
    {
        public event Action? ResultChanged { add { } remove { } }
        public T Result => result;
    }
}
