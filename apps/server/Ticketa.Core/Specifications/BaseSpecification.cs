using System.Linq.Expressions;

namespace Ticketa.Core.Specifications
{
  public class BaseSpecification<T>
  {
    public List<Expression<Func<T, bool>>> CriteriaList { get; } = new List<Expression<Func<T, bool>>>();
    public Expression<Func<T, bool>>? Criteria
    {
      get
      {
        if (CriteriaList.Count == 0) return null;
        if (CriteriaList.Count == 1) return CriteriaList[0];

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? combined = null;

        foreach (var expr in CriteriaList)
        {
          var visitor = new ParameterReplacer(expr.Parameters[0], parameter);
          var body = visitor.Visit(expr.Body);
          combined = combined == null ? body : Expression.AndAlso(combined, body);
        }

        return Expression.Lambda<Func<T, bool>>(combined!, parameter);
      }
    }
    public List<Expression<Func<T, object>>> Includes { get; } = new List<Expression<Func<T, object>>>();
    public List<string> IncludeStrings { get; } = new List<string>();
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDesc { get; private set; }
    public int Take { get; private set; }
    public int Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }
    public bool AsNoTracking { get; protected set; }
    public bool AsSplitQuery { get; protected set; }

    protected void AddCriteria(Expression<Func<T, bool>> criteria) => CriteriaList.Add(criteria);

    protected void AddInclude(Expression<Func<T, object>> includeExpression) => Includes.Add(includeExpression);
    protected void AddInclude(string includeString) => IncludeStrings.Add(includeString);

    protected void AddOrderBy(Expression<Func<T, object>> orderBy) => OrderBy = orderBy;

    protected void AddOrderByDesc(Expression<Func<T, object>> orderByDesc) => OrderByDesc = orderByDesc;

    protected void ApplyNoTracking() => AsNoTracking = true;
    protected void ApplySplitQuery() => AsSplitQuery = true;

    protected void ApplyPaging(int skip, int take)
    {
      Skip = skip;
      Take = take;
      IsPagingEnabled = true;
    }

    private sealed class ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam) : ExpressionVisitor
    {
      protected override Expression VisitParameter(ParameterExpression node) => node == oldParam ? newParam : base.VisitParameter(node);
    }
  }
}
