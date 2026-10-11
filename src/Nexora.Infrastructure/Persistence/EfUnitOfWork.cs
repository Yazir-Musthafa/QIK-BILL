using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nexora.Application.Abstractions;
using Nexora.Domain.Audit;

namespace Nexora.Infrastructure.Persistence;

internal sealed class EfUnitOfWorkFactory(IDbContextFactory<NexoraDbContext> contexts) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> BeginAsync(bool readOnly = false, CancellationToken cancellationToken = default)
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken);
        try
        {
            // On SQLite this issues BEGIN IMMEDIATE, so writers queue on the busy timeout instead of
            // failing later when a read lock would have to be upgraded.
            var transaction = readOnly ? null : await context.Database.BeginTransactionAsync(cancellationToken);
            return new EfUnitOfWork(context, transaction, readOnly);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}

/// <summary>All queries go through the context's company filters; there is deliberately no way to bypass them here.</summary>
internal sealed class EfUnitOfWork(NexoraDbContext context, IDbContextTransaction? transaction, bool readOnly) : IUnitOfWork
{
    private bool _committed;

    public Task<T?> FindAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : class =>
        Query<T>().FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

    public Task<List<T>> ListAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class =>
        (predicate is null ? Query<T>() : Query<T>().Where(predicate)).ToListAsync(cancellationToken);

    public Task<List<T>> ListAsync<T, TKey>(Expression<Func<T, bool>>? predicate, Expression<Func<T, TKey>> orderBy, bool descending,
        int skip, int take, CancellationToken cancellationToken = default) where T : class
    {
        var query = predicate is null ? Query<T>() : Query<T>().Where(predicate);
        query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        if (skip > 0) query = query.Skip(skip);
        if (take < int.MaxValue) query = query.Take(take);
        return query.ToListAsync(cancellationToken);
    }

    public Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class =>
        Query<T>().AnyAsync(predicate, cancellationToken);

    public Task<List<TResult>> QueryAsync<TResult>(Func<IQuerySource, IQueryable<TResult>> shape, CancellationToken cancellationToken = default) =>
        shape(new Source(context)).ToListAsync(cancellationToken);

    public IAsyncEnumerable<TResult> StreamAsync<TResult>(Func<IQuerySource, IQueryable<TResult>> shape, CancellationToken cancellationToken = default) =>
        shape(new Source(context)).AsAsyncEnumerable();

    private sealed class Source(NexoraDbContext context) : IQuerySource
    {
        public IQueryable<T> Set<T>() where T : class => context.Set<T>().AsNoTracking();
    }

    public void Add<T>(T entity) where T : class
    {
        EnsureWritable();
        context.Add(entity);
    }

    public void Remove<T>(T entity) where T : class
    {
        EnsureWritable();
        context.Remove(entity);
    }

    public void Record(AuditEvent auditEvent)
    {
        EnsureWritable();
        context.QueueEvent(auditEvent);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        if (_committed) throw new InvalidOperationException("This unit of work has already been committed.");
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("Someone else changed or removed this record. Refresh and try again.");
        }

        await transaction!.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        // Disposing an uncommitted transaction rolls it back.
        if (transaction is not null) await transaction.DisposeAsync();
        await context.DisposeAsync();
    }

    private IQueryable<T> Query<T>() where T : class => readOnly ? context.Set<T>().AsNoTracking() : context.Set<T>();

    private void EnsureWritable()
    {
        if (readOnly) throw new InvalidOperationException("This unit of work is read-only.");
    }
}
