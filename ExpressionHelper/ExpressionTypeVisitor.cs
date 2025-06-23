using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace ExpressionHelper
{
    public class ExpressionChangeVisitorParams
    {
        public (Type From, Type To)[] TypeChange { get; set; } = [];

        public Func<object?, object?> ConstantChangeFunction { get; set; } = (x) => x;
        public Func<MemberInfo?, MemberInfo?> MemberChangeFunction { get; set; } = (x) => x;
    }

    public static class ExpressionChangeVisitor
    {
        private class ExpressionChangeVisitorVisitReturn
        {
            public Expression? Expression { get; set; }
            public List<(Expression From, Expression To)> ChangedParameters { get; set; } = new List<(Expression From, Expression To)>();
        }

        [return: NotNullIfNotNull(nameof(node))]
        public static Expression? Visit(Expression? node, ExpressionChangeVisitorParams @params)
        {
            return InternalVisit(node, @params)?.Expression;
        }

        [return: NotNullIfNotNull(nameof(node))]
        private static ExpressionChangeVisitorVisitReturn? InternalVisit(Expression? node, ExpressionChangeVisitorParams @params)
        {
            if (node == null)
            {
                return null;
            }

            FillDefaultParams(@params);

            return InternalVisit(
                new ExpressionChangeVisitorVisitReturn()
                {
                    Expression = node
                }, @params);
        }

        private static ExpressionChangeVisitorVisitReturn InternalVisit(ExpressionChangeVisitorVisitReturn node, ExpressionChangeVisitorParams @params)
        {
            FillDefaultParams(@params);

            if (node.Expression == null)
            {
                return node;
            }

            if(node.Expression is MethodCallExpression)
            {
                return VisitMethodCall(node, @params);
            }

            if (node.Expression is LambdaExpression)
            {
                return VisitLambda(node, @params);
            }

            if(node.Expression is ParameterExpression)
            {
                return VisitParameter(node, @params);
            }

            if (node.Expression is MemberExpression)
            {
                return VisitMember(node, @params);
            }

            if(node.Expression is ConstantExpression)
            {
                return VisitConstant(node, @params);
            }

            if (node.Expression is UnaryExpression)
            {
                return VisitUnary(node, @params);
            }

            if (node.Expression is BinaryExpression)
            {
                return VisitBinary(node, @params);
            }

            throw new Exception("ExpressionChangeVisitor: Can't find nodeType:"+node.Expression.NodeType);

        }

        private static ExpressionChangeVisitorVisitReturn VisitMethodCall(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            if (nodeArgs.Expression is MethodCallExpression node)
            {
                var oldArgs = node.Arguments;
                var oldMethod = node.Method;
                var oldObject = node.Object;
                var newArgs = oldArgs?.Select(x =>
                {
                    nodeArgs.Expression = x;
                    return InternalVisit(nodeArgs, @params).Expression;
                })?.ToArray();

                nodeArgs.Expression = oldObject;
                var newObject = InternalVisit(nodeArgs, @params).Expression;

                if (oldMethod.IsGenericMethod)
                {
                    var newMethod = MakeGenericMethod(oldMethod, @params);
                    newArgs = MakeArgsForCall(newArgs, newMethod.GetParameters().Select(x => x.ParameterType)).ToArray();
                    var newNode = Expression.Call(newObject, newMethod, newArgs);
                    nodeArgs.Expression = newNode;
                    return nodeArgs;
                }
                else
                {
                    nodeArgs.Expression = node.Update(newObject, newArgs);
                    return nodeArgs;
                }
            }
            return nodeArgs;
        }

        private static IEnumerable<Expression> MakeArgsForCall(IEnumerable<Expression> args,IEnumerable<Type> argNeedTypes)
        {
            var needTypes = argNeedTypes.ToArray();
            foreach (var arg in args.Select((value, i) => (value, i)))
            {
                var needType = needTypes[arg.i];
                yield return MakeArgForCall(arg.value, needType);
            }
        }

        private static Expression MakeArgForCall(Expression node, Type argNeedType)
        {
            if (node is LambdaExpression lambdaExpression)
            {
                if (argNeedType.IsAssignableTo(typeof(Expression)))
                {
                    if (argNeedType.IsGenericType)
                    {
                        argNeedType = argNeedType.GetGenericArguments()[0];
                    }
                }
                return Expression.Lambda(argNeedType, lambdaExpression.Body, lambdaExpression.TailCall, lambdaExpression.Parameters);
            }

            if(node is UnaryExpression unaryExpression)
            {
                return unaryExpression.Update(MakeArgForCall(unaryExpression.Operand, argNeedType));
            }

            return node;
        }

        private static ExpressionChangeVisitorVisitReturn VisitLambda(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            if (nodeArgs.Expression is LambdaExpression node)
            {
                var oldParams = node.Parameters;
                var oldBody = node.Body;

                var newParams = oldParams.Select(x =>
                {
                    nodeArgs.Expression = x;
                    return InternalVisit(nodeArgs, @params).Expression as ParameterExpression;
                })?.ToArray();

                nodeArgs.Expression = oldBody;
                var newBody = InternalVisit(nodeArgs, @params).Expression;

                nodeArgs.Expression = Expression.Lambda(newBody, node.Name, node.TailCall, newParams);
                return nodeArgs;
            }
            return nodeArgs;
        }

        private static ExpressionChangeVisitorVisitReturn VisitMember(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            if (nodeArgs.Expression is MemberExpression node)
            {

                var oldContaining = node.Expression;
                var oldMember = node.Member;

                nodeArgs.Expression = oldContaining;
                var newContaining = InternalVisit(nodeArgs, @params).Expression;
                var newMember = oldMember;
                newMember = @params.MemberChangeFunction(oldMember);

                nodeArgs.Expression = Expression.MakeMemberAccess(newContaining, newMember);
                return nodeArgs;
            }
            return nodeArgs;
        }

        private static ExpressionChangeVisitorVisitReturn VisitParameter(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            var find = nodeArgs.ChangedParameters.FirstOrDefault(x => x.From == nodeArgs.Expression);
            if (find.To != null)
            {
                nodeArgs.Expression = find.To;
                return nodeArgs;
            }

            if (nodeArgs.Expression is ParameterExpression node)
            {
                var oldType = node.Type;
                var newType = MakeType(oldType, @params);
                if (node.IsByRef)
                {
                    nodeArgs.Expression = Expression.Parameter(newType, node.Name);
                }
                else
                {
                    nodeArgs.Expression = Expression.Variable(newType, node.Name);
                }

                nodeArgs.ChangedParameters.Add((From: node, To: nodeArgs.Expression));
                return nodeArgs;
            }
            return nodeArgs;
        }

        private static ExpressionChangeVisitorVisitReturn VisitConstant(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            var find = nodeArgs.ChangedParameters.FirstOrDefault(x => x.From == nodeArgs.Expression);
            if (find.To != null)
            {
                nodeArgs.Expression = find.To;
                return nodeArgs;
            }

            if (nodeArgs.Expression is ConstantExpression node)
            {
                var oldValue = node.Value;
                var newValue = @params.ConstantChangeFunction(node.Value);

                nodeArgs.Expression = Expression.Constant(newValue);
                nodeArgs.ChangedParameters.Add((From: node, To: nodeArgs.Expression));

                return nodeArgs;
            }
            return nodeArgs;
        }

        private static ExpressionChangeVisitorVisitReturn VisitBinary(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            if (nodeArgs.Expression is BinaryExpression node)
            {
                var oldLeft = node.Left;
                var oldConvertion = node.Conversion;
                var oldRight = node.Right;
                var oldMethod = node.Method;

                nodeArgs.Expression = oldLeft;
                var newLeft = InternalVisit(nodeArgs, @params).Expression;

                nodeArgs.Expression = oldRight;
                var newRight = InternalVisit(nodeArgs, @params).Expression;

                nodeArgs.Expression = oldConvertion;
                var newConversion = InternalVisit(nodeArgs, @params).Expression as LambdaExpression;
                var newMethod = MakeGenericMethod(oldMethod, @params);

                nodeArgs.Expression = Expression.MakeBinary(node.NodeType, newLeft, newRight, node.IsLiftedToNull, newMethod, newConversion);
                return nodeArgs;
            }
            return nodeArgs;
        }

        private static ExpressionChangeVisitorVisitReturn VisitUnary(ExpressionChangeVisitorVisitReturn nodeArgs, ExpressionChangeVisitorParams @params)
        {
            if (nodeArgs.Expression is UnaryExpression node)
            {
                var oldOperand = node.Operand;
                var oldMethod = node.Method;
                var oldType = node.Type;

                nodeArgs.Expression = oldOperand;
                var newOperand = InternalVisit(nodeArgs, @params).Expression;
                var newMethod = MakeGenericMethod(oldMethod, @params);
                var newType = node.NodeType == ExpressionType.Quote ? oldType : MakeType(oldType, @params);

                nodeArgs.Expression = Expression.MakeUnary(node.NodeType, newOperand, newType, newMethod);
                return nodeArgs;
            }
            return nodeArgs;
        }

        [return: NotNullIfNotNull(nameof(info))]
        private static MethodInfo? MakeGenericMethod(MethodInfo? info, ExpressionChangeVisitorParams @params)
        {
            if (info == null)
            {
                return info;
            }

            if (info.IsGenericMethod)
            {
                var def = info.GetGenericMethodDefinition();
                var newGenericArgs = info.GetGenericArguments().Select(x =>
                {
                    return MakeType(x,@params);
                }).ToArray();
                return def.MakeGenericMethod(newGenericArgs);
            }
            return info;
        }

        [return: NotNullIfNotNull(nameof(type))]
        private static Type? MakeType(Type? type, ExpressionChangeVisitorParams @params)
        {
            if (type == null)
            {
                return type;
            }

            var find = @params.TypeChange.FirstOrDefault(x=>x.From.Name==type?.Name);
            if (find.To!=null)
            {
                return find.To;
            }

            if (type.IsGenericType)
            {
                var oldArgs = type.GetGenericArguments();
                var newArgs = oldArgs.Select(x => MakeType(x, @params)).ToArray();
                var def = type.GetGenericTypeDefinition();
                return def.MakeGenericType(newArgs);
            }

            return type;
        }

        private static void FillDefaultParams(ExpressionChangeVisitorParams @params)
        {
            var oldMemberChangeFunction = @params.MemberChangeFunction;
            @params.MemberChangeFunction = (x) =>
            {
                if (oldMemberChangeFunction != null)
                {
                    var newMember = oldMemberChangeFunction(x);
                    if (newMember != null)
                    {
                        return newMember;
                    }
                }

                if (x == null)
                {
                    return x;
                }

                var declType = x.DeclaringType;

                if (declType == null)
                {
                    return x;
                }

                var newType = MakeType(declType, @params);
                if (newType.Name == declType.Name)
                {
                    return x;
                }

                var prop = newType.GetProperty(x.Name);
                if (prop != null)
                {
                    return prop;
                }

                var field = newType.GetField(x.Name);

                if (prop != null)
                {
                    return prop;
                }

                throw new Exception("ExpressionChangeVisitor: Can't convert member expression" + x +" from type "+x.Name+" to "+newType.Name);
            };
        }
    }
}
