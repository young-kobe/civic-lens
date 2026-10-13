using System.Linq.Expressions;
using CivicLens.Application.Paging;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewKeyset
{
    private static readonly System.Reflection.MethodInfo Compare =
        typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    public static IQueryable<T> Page<T>(IQueryable<T> rows, PageCursor? cursor, Expression<Func<T, long>> ticksOf,
        Expression<Func<T, string>> idOf)
    {
        if (cursor is null) return rows.OrderByDescending(ticksOf).ThenByDescending(idOf);
        var (ticks, id) = (cursor.Ticks, cursor.Id);
        var ticksValue = ((Expression<Func<long>>)(() => ticks)).Body;
        var idValue = ((Expression<Func<string>>)(() => id)).Body;
        var row = ticksOf.Parameters[0];
        var rowTicks = ticksOf.Body;
        var rowId = new ParameterReplacer(idOf.Parameters[0], row).Visit(idOf.Body);
        var idOrder = Expression.Call(Compare, rowId, idValue);
        var older = cursor.Direction == PageDirection.Older;
        var beyond = Expression.OrElse(
            older ? Expression.LessThan(rowTicks, ticksValue) : Expression.GreaterThan(rowTicks, ticksValue),
            Expression.AndAlso(Expression.Equal(rowTicks, ticksValue),
                older ? Expression.LessThan(idOrder, Expression.Constant(0)) : Expression.GreaterThan(idOrder, Expression.Constant(0))));
        var window = rows.Where(Expression.Lambda<Func<T, bool>>(beyond, row));
        return older ? window.OrderByDescending(ticksOf).ThenByDescending(idOf) : window.OrderBy(ticksOf).ThenBy(idOf);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
