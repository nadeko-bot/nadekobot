using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public sealed class TodoService : INService
{
    private const int ARCHIVE_MAX_COUNT = 18;
    private const int TODO_MAX_COUNT = 36;

    private readonly DbService _db;

    public TodoService(DbService db)
    {
        _db = db;
    }

    public async Task<TodoAddResult> AddAsync(ulong userId, string todo)
    {
        await using var ctx = _db.GetDbContext();

        if (await ctx
                  .GetTable<TodoModel>()
                  .Where(x => x.UserId == userId && x.ArchiveId == null)
                  .CountAsync()
            >= TODO_MAX_COUNT)
        {
            return TodoAddResult.MaxLimitReached;
        }

        await ctx
              .GetTable<TodoModel>()
              .InsertAsync(() => new TodoModel()
              {
                  UserId = userId,
                  Todo = todo,
                  DateAdded = DateTime.UtcNow,
                  IsDone = false,
              });

        return TodoAddResult.Success;
    }

    public async Task<bool> EditAsync(ulong userId, int todoId, string newMessage)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx
                     .GetTable<TodoModel>()
                     .Where(x => x.UserId == userId && x.Id == todoId)
                     .Set(x => x.Todo, newMessage)
                     .UpdateAsync()
               > 0;
    }

    public async Task<TodoModel[]> GetAllTodosAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx
                     .GetTable<TodoModel>()
                     .Where(x => x.UserId == userId && x.ArchiveId == null)
                     .ToArrayAsyncLinqToDB();
    }

    public async Task<bool> CompleteTodoAsync(ulong userId, int todoId)
    {
        await using var ctx = _db.GetDbContext();

        var count = await ctx
                          .GetTable<TodoModel>()
                          .Where(x => x.UserId == userId && x.Id == todoId)
                          .Set(x => x.IsDone, true)
                          .UpdateAsync();

        return count > 0;
    }

    public async Task<bool> UncompleteTodoAsync(ulong userId, int todoId)
    {
        await using var ctx = _db.GetDbContext();

        var count = await ctx
                          .GetTable<TodoModel>()
                          .Where(x => x.UserId == userId && x.Id == todoId)
                          .Set(x => x.IsDone, false)
                          .UpdateAsync();

        return count > 0;
    }

    public async Task<bool> DeleteTodoAsync(ulong userId, int todoId)
    {
        await using var ctx = _db.GetDbContext();

        var count = await ctx
                          .GetTable<TodoModel>()
                          .Where(x => x.UserId == userId && x.Id == todoId)
                          .DeleteAsync();

        return count > 0;
    }

    public async Task ClearTodosAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        await ctx
              .GetTable<TodoModel>()
              .Where(x => x.UserId == userId && x.ArchiveId == null)
              .DeleteAsync();
    }

    public async Task<ArchiveTodoResult> ArchiveTodosAsync(ulong userId, string name, bool onlyDone)
    {
        // create a new archive

        await using var ctx = _db.GetDbContext();

        await using var tr = await ctx.Database.BeginTransactionAsync();

        // check if the user reached the limit
        var count = await ctx
                          .GetTable<ArchivedTodoListModel>()
                          .Where(x => x.UserId == userId)
                          .CountAsync();

        if (count >= ARCHIVE_MAX_COUNT)
            return ArchiveTodoResult.MaxLimitReached;

        var inserted = await ctx
                             .GetTable<ArchivedTodoListModel>()
                             .InsertWithOutputAsync(() => new ArchivedTodoListModel()
                             {
                                 UserId = userId,
                                 Name = name,
                             });

        // mark all existing todos as archived

        var updated = await ctx
                            .GetTable<TodoModel>()
                            .Where(x => x.UserId == userId && (!onlyDone || x.IsDone) && x.ArchiveId == null)
                            .Set(x => x.ArchiveId, inserted.Id)
                            .UpdateAsync();

        if (updated == 0)
        {
            await tr.RollbackAsync();
            // // delete the empty archive
            // await ctx
            //     .GetTable<ArchivedTodoListModel>()
            //     .Where(x => x.Id == inserted.Id)
            //     .DeleteAsync();

            return ArchiveTodoResult.NoTodos;
        }

        await tr.CommitAsync();

        return ArchiveTodoResult.Success;
    }


    public async Task<IReadOnlyCollection<ArchivedTodoListModel>> GetArchivedTodosAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx
                     .GetTable<ArchivedTodoListModel>()
                     .Where(x => x.UserId == userId)
                     .ToArrayAsyncLinqToDB();
    }

    public async Task<ArchivedTodoListModel?> GetArchivedTodoListAsync(ulong userId, int archiveId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx
                     .GetTable<ArchivedTodoListModel>()
                     .Where(x => x.UserId == userId && x.Id == archiveId)
                     .LoadWith(x => x.Items)
                     .FirstOrDefaultAsyncLinqToDB();
    }

    public async Task<bool> ArchiveDeleteAsync(ulong userId, int archiveId)
    {
        await using var ctx = _db.GetDbContext();

        var count = await ctx
                          .GetTable<ArchivedTodoListModel>()
                          .Where(x => x.UserId == userId && x.Id == archiveId)
                          .DeleteAsync();

        return count > 0;
    }

    public async Task<TodoModel?> GetTodoAsync(ulong userId, int todoId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx
                     .GetTable<TodoModel>()
                     .Where(x => x.UserId == userId && x.Id == todoId)
                     .FirstOrDefaultAsyncLinqToDB();
    }

}