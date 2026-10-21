using System.Linq.Expressions;
using CivicLens.Application.Paging;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewKeyset
{
    private static readonly System.Reflection.MethodInfo Compare =
        typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    public static long TicksOf(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).Ticks;

    private static DateOnly DateOfTicks(long ticks) => DateOnly.FromDateTime(new DateTime(ticks));

    public static IQueryable<T> Page<T>(IQueryable<T> rows, PageCursor? cursor, Expression<Func<T, long>> ticksOf,
        Expression<Func<T, string>> idOf, bool ascending = false)
    {
        PageCursor.RequireRank(cursor, ranked: false);
        if (cursor is null) return Ordered(rows, ticksOf, idOf, ascending);
        var older = cursor.Direction == PageDirection.Older;
        var window = rows.Where(Beyond<T>(older, Key(ticksOf, cursor.Ticks, !ascending), Key(idOf, cursor.Id, !ascending)));
        return Ordered(window, ticksOf, idOf, ascending == older);
    }

    private static IQueryable<T> Ordered<T>(IQueryable<T> rows, Expression<Func<T, long>> ticksOf, Expression<Func<T, string>> idOf,
        bool ascending) =>
        ascending ? rows.OrderBy(ticksOf).ThenBy(idOf) : rows.OrderByDescending(ticksOf).ThenByDescending(idOf);

    public static IQueryable<T> Page<T>(IQueryable<T> rows, PageCursor? cursor, Expression<Func<T, int>> rankOf,
        Expression<Func<T, DateOnly>> dateOf, Expression<Func<T, string>> idOf)
    {
        PageCursor.RequireRank(cursor, ranked: true);
        if (cursor is null) return rows.OrderBy(rankOf).ThenByDescending(dateOf).ThenByDescending(idOf);
        var older = cursor.Direction == PageDirection.Older;
        var window = rows.Where(Beyond<T>(older, Key(rankOf, cursor.Rank!.Value, false),
            Key(dateOf, DateOfTicks(cursor.Ticks), true), Key(idOf, cursor.Id, true)));
        return older
            ? window.OrderBy(rankOf).ThenByDescending(dateOf).ThenByDescending(idOf)
            : window.OrderByDescending(rankOf).ThenBy(dateOf).ThenBy(idOf);
    }

    private static Expression<Func<T, bool>> Beyond<T>(bool older, params KeyPart<T>[] keys)
    {
        var row = keys[0].Selector.Parameters[0];
        Expression? beyond = null;
        foreach (var key in keys.Reverse())
        {
            var value = new ParameterReplacer(key.Selector.Parameters[0], row).Visit(key.Selector.Body);
            var strictly = Strictly(value, key.Value, older == key.Descending);
            beyond = beyond is null ? strictly : Expression.OrElse(strictly, Expression.AndAlso(Expression.Equal(value, key.Value), beyond));
        }
        return Expression.Lambda<Func<T, bool>>(beyond!, row);
    }

    private static Expression Strictly(Expression row, Expression value, bool less)
    {
        if (row.Type == typeof(string))
        {
            var order = Expression.Call(Compare, row, value);
            return less ? Expression.LessThan(order, Expression.Constant(0)) : Expression.GreaterThan(order, Expression.Constant(0));
        }
        return less ? Expression.LessThan(row, value) : Expression.GreaterThan(row, value);
    }

    private static KeyPart<T> Key<T, TValue>(Expression<Func<T, TValue>> selector, TValue value, bool descending) =>
        new(selector, Expression.Property(Expression.Constant(new Parameter<TValue>(value)), nameof(Parameter<TValue>.Value)),
            descending);

    private sealed record KeyPart<T>(LambdaExpression Selector, Expression Value, bool Descending);

    private sealed record Parameter<TValue>(TValue Value);

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
