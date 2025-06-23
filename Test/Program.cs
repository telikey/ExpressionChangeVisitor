using ExpressionHelper;
using System.Linq.Expressions;

namespace Test
{
    public class TestClassItemFrom
    {
        public long Id { get; set; }
        public long? GroupId { get; set; }
    }

    public class TestClassGroupFrom
    {
        public long Id { get; set; }
        public long? ParentId { get; set; }
    }

    public class TestClassItemTo
    {
        public long Id { get; set; }
        public long? GroupId { get; set; }
    }

    public class TestClassGroupTo
    {
        public long Id { get; set; }
        public long? ParentId { get; set; }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            var queryItems = (new[] { 
                new TestClassItemTo() { Id=1, GroupId = 1 }, 
                new TestClassItemTo() { Id=2, GroupId = 1 }, 
                new TestClassItemTo() { Id = 3,GroupId=1 } 
            }).AsQueryable();

            var queryGroups = (new[] { 
                new TestClassGroupTo() { Id = 1} 
            }).AsQueryable();

            Expression exp = GenerateExpression<IQueryable<TestClassItemFrom>,IQueryable<TestClassGroupFrom>, IQueryable<TestClassGroupFrom>>(
                (x,y)=>x.SelectMany(i=>y.Where(j=>i.GroupId==j.Id)).DistinctBy(x=>x.Id));

            var visitorParams = new ExpressionChangeVisitorParams()
            {
                TypeChange = new[] {
                    (From:typeof(TestClassItemFrom),To:typeof(TestClassItemTo)),
                    (From:typeof(TestClassGroupFrom),To:typeof(TestClassGroupTo))
                }
            };

            var newExp = ExpressionChangeVisitor.Visit(exp, visitorParams);

            var compiledExp = (newExp as LambdaExpression).Compile(true);
            var res = compiledExp.DynamicInvoke(queryItems, queryGroups);

            Console.ReadKey();
        }

        static Expression GenerateExpression<T1,TOut>(Expression<Func<T1, TOut>> exp)
        {
            return exp;
        }

        static Expression GenerateExpression<T1,T2, TOut>(Expression<Func<T1, T2, TOut>> exp)
        {
            return exp;
        }

        static Expression GenerateExpression<T1, T2, T3, TOut>(Expression<Func<T1, T2, T3, TOut>> exp)
        {
            return exp;
        }
    }
}
