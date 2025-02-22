using NadekoBot.Db.Models;
using System.Text;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group("todo")]
    public partial class Todo : NadekoModule<TodoService>
    {
        [Cmd]
        public async Task TodoAdd([Leftover] string todo)
        {
            var result = await _service.AddAsync(ctx.User.Id, todo);
            if (result == TodoAddResult.MaxLimitReached)
            {
                await Response().Error(strs.todo_add_max_limit).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }

        [Cmd]
        public async Task TodoEdit(kwum todoId, [Leftover] string newMessage)
        {
            if (!await _service.EditAsync(ctx.User.Id, todoId, newMessage))
            {
                await Response().Error(strs.todo_not_found).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }

        [Cmd]
        public async Task TodoList()
        {
            var todos = await _service.GetAllTodosAsync(ctx.User.Id);

            if (todos.Length == 0)
            {
                await Response().Error(strs.todo_list_empty).SendAsync();
                return;
            }

            await Response()
                  .Paginated()
                  .Items(todos)
                  .PageSize(9)
                  .AddFooter(false)
                  .Page((items, _) =>
                  {
                      var eb = CreateEmbed()
                                      .WithOkColor()
                                      .WithTitle(GetText(strs.todo_list));

                      ShowTodoItem(items, eb);

                      eb.WithFooter(GetText(strs.todo_stats(todos.Length,
                          todos.Count(x => x.IsDone),
                          todos.Count(x => !x.IsDone))));

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        public async Task TodoShow(kwum todoId)
        {
            var todo = await _service.GetTodoAsync(ctx.User.Id, todoId);

            if (todo is null)
            {
                await Response().Error(strs.todo_not_found).SendAsync();
                return;
            }

            await Response()
                  .Confirm($"`{new kwum(todo.Id)}` {todo.Todo}")
                  .SendAsync();
        }


        [Cmd]
        public async Task TodoComplete(kwum todoId)
        {
            if (!await _service.CompleteTodoAsync(ctx.User.Id, todoId))
            {
                await Response().Error(strs.todo_not_found).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }
        
        [Cmd]
        public async Task TodoUncomplete(kwum todoId)
        {
            if (!await _service.UncompleteTodoAsync(ctx.User.Id, todoId))
            {
                await Response().Error(strs.todo_not_found).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }

        [Cmd]
        public async Task TodoDelete(kwum todoId)
        {
            if (!await _service.DeleteTodoAsync(ctx.User.Id, todoId))
            {
                await Response().Error(strs.todo_not_found).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }

        [Cmd]
        public async Task TodoClear()
        {
            await _service.ClearTodosAsync(ctx.User.Id);

            await Response().Confirm(strs.todo_cleared).SendAsync();
        }


        private static void ShowTodoItem(IReadOnlyCollection<TodoModel> todos, EmbedBuilder eb)
        {
            var sb = new StringBuilder();
            foreach (var todo in todos)
            {
                sb.AppendLine(InternalItemShow(todo));

                sb.AppendLine("---");
            }

            eb.WithDescription(sb.ToString());
        }

        private static string InternalItemShow(TodoModel todo)
            => $"{(todo.IsDone ? "✔" : "□")} {Format.Code(new kwum(todo.Id).ToString())} {todo.Todo}";

        [Group("archive")]
        public partial class ArchiveCommands : NadekoModule<TodoService>
        {
            [Cmd]
            public async Task TodoArchiveAdd([Leftover] string name)
            {
                var result = await _service.ArchiveTodosAsync(ctx.User.Id, name, false);
                if (result == ArchiveTodoResult.NoTodos)
                {
                    await Response().Error(strs.todo_no_todos).SendAsync();
                    return;
                }

                if (result == ArchiveTodoResult.MaxLimitReached)
                {
                    await Response().Error(strs.todo_archive_max_limit).SendAsync();
                    return;
                }

                await ctx.OkAsync();
            }
            
            [Cmd]
            public async Task TodoArchiveDone([Leftover] string name)
            {
                var result = await _service.ArchiveTodosAsync(ctx.User.Id, name, true);
                if (result == ArchiveTodoResult.NoTodos)
                {
                    await Response().Error(strs.todo_no_todos).SendAsync();
                    return;
                }

                if (result == ArchiveTodoResult.MaxLimitReached)
                {
                    await Response().Error(strs.todo_archive_max_limit).SendAsync();
                    return;
                }

                await ctx.OkAsync();
            }

            [Cmd]
            public async Task TodoArchiveList(int page = 1)
            {
                if (--page < 0)
                    return;

                var archivedTodoLists = await _service.GetArchivedTodosAsync(ctx.User.Id);

                if (archivedTodoLists.Count == 0)
                {
                    await Response().Error(strs.todo_archive_empty).SendAsync();
                    return;
                }

                await Response()
                      .Paginated()
                      .Items(archivedTodoLists)
                      .PageSize(9)
                      .CurrentPage(page)
                      .Page((items, _) =>
                      {
                          var eb = CreateEmbed()
                                          .WithTitle(GetText(strs.todo_archive_list))
                                          .WithOkColor();

                          foreach (var archivedList in items)
                          {
                              eb.AddField($"id: {new kwum(archivedList.Id)}", archivedList.Name, true);
                          }

                          return eb;
                      })
                      .SendAsync();
            }

            [Cmd]
            public async Task TodoArchiveShow(kwum id)
            {
                var list = await _service.GetArchivedTodoListAsync(ctx.User.Id, id);
                if (list == null || list.Items.Count == 0)
                {
                    await Response().Error(strs.todo_archive_not_found).SendAsync();
                    return;
                }

                await Response()
                      .Paginated()
                      .Items(list.Items)
                      .PageSize(9)
                      .AddFooter(false)
                      .Page((items, _) =>
                      {
                          var eb = CreateEmbed()
                                          .WithOkColor()
                                          .WithTitle(GetText(strs.todo_archived_list));

                          ShowTodoItem(items, eb);

                          eb.WithFooter(GetText(strs.todo_stats(list.Items.Count,
                              list.Items.Count(x => x.IsDone),
                              list.Items.Count(x => !x.IsDone))));

                          return eb;
                      })
                      .SendAsync();
            }

            [Cmd]
            public async Task TodoArchiveDelete(kwum id)
            {
                if (!await _service.ArchiveDeleteAsync(ctx.User.Id, id))
                {
                    await ctx.ErrorAsync();
                    return;
                }

                await ctx.OkAsync();
            }
        }
    }
}