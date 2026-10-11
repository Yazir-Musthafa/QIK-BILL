using System.Linq.Expressions;
using Nexora.Domain.Audit;

namespace Nexora.Application.Abstractions;

/// <summary>
/// Provider-neutral unit of work for company-scoped data. Every query is limited to the signed-in
/// company by Infrastructure; entities loaded through a writable unit of work are tracked, so changes
/// (and their old values, for the audit trail) are detected on <see cref="CommitAsync"/>.
/// A writable unit of work holds the database write lock until it is disposed, so keep it short.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task<T?> FindAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : class;

    Task<List<T>> ListAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Ordered page of results, for example the newest audit entries.</summary>
    Task<List<T>> ListAsync<T, TKey>(Expression<Func<T, bool>>? predicate, Expression<Func<T, TKey>> orderBy, bool descending,
        int skip, int take, CancellationToken cancellationToken = default) where T : class;

    Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Runs a query shaped by the caller in the database (filters, joins, grouping, sums, paging), for reports over large
    /// data. Every set is limited to the signed-in company, like the other queries.
    /// </summary>
    Task<List<TResult>> QueryAsync<TResult>(Func<IQuerySource, IQueryable<TResult>> shape, CancellationToken cancellationToken = default);

    /// <summary>Streams a database-shaped query row by row, so a large result never sits in memory at once.</summary>
    IAsyncEnumerable<TResult> StreamAsync<TResult>(Func<IQuerySource, IQueryable<TResult>> shape, CancellationToken cancellationToken = default);

    void Add<T>(T entity) where T : class;

    void Remove<T>(T entity) where T : class;

    /// <summary>Queues an explicit audit event, stored atomically with the other changes.</summary>
    void Record(AuditEvent auditEvent);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Company-filtered, untracked sets to compose a database query from.</summary>
public interface IQuerySource
{
    IQueryable<T> Set<T>() where T : class;
}

public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Starts a unit of work. A writable one begins a transaction immediately so concurrent writers are
    /// serialised; a read-only one does not track entities and cannot commit.
    /// </summary>
    Task<IUnitOfWork> BeginAsync(bool readOnly = false, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a save conflicts with data changed or removed by someone else.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);
