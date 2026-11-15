using System.Reflection;
using System.Text;
using log4net;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

namespace Server.Headless
{
    public sealed class BanRequest
    {
        public string Reason { get; set; } = string.Empty;
        public int Days { get; set; }
    }

    public sealed class PasswordRequest
    {
        public string Password { get; set; } = string.Empty;
    }

    public sealed class MessageRequest
    {
        public string Name { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class AdminPasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public static class AdminApi
    {
        private static readonly ILog WebLog = LogManager.GetLogger(Assembly.GetEntryAssembly(), "Web");

        public static void MapAdminApi(this WebApplication app)
        {
            var api = app.MapGroup("/api").RequireAuthorization();

            api.MapGet("/status", (WebAdminSettings web) =>
            {
                var envir = Envir.Main;
                return Results.Ok(new
                {
                    running = envir.Running,
                    uptimeSeconds = AdminRuntime.UptimeSeconds,
                    players = envir.Players.Count,
                    accounts = envir.AccountList.Count,
                    maps = envir.MapList.Count,
                    monsters = envir.MonsterCount,
                    serverIp = Settings.IPAddress,
                    serverPort = Settings.Port,
                    webAddress = $"{web.BindAddress}:{web.Port}",
                    dbVersion = Envir.Version,
                    loadedVersion = Envir.LoadVersion,
                    mustChangePassword = web.MustChangePassword,
                    adminUser = web.Username
                });
            });

            api.MapPost("/server/start", () =>
            {
                if (Envir.Main.Running) return Results.Ok(new { ok = true, message = "服务端已在运行" });

                Envir.Main.Start();
                AdminRuntime.StartedAt = DateTime.UtcNow;
                WebLog.Info("Server start requested from web admin");
                return Results.Ok(new { ok = Envir.Main.Running, message = Envir.Main.Running ? "服务端已启动" : "启动请求已发送" });
            });

            api.MapPost("/server/stop", () =>
            {
                Envir.Main.Stop();
                AdminRuntime.StartedAt = null;
                WebLog.Info("Server stop requested from web admin");
                return Results.Ok(new { ok = true, message = "服务端正在停止" });
            });

            api.MapGet("/accounts", (string q, bool? banned, int? page, int? pageSize) =>
            {
                var currentPage = Math.Max(1, page ?? 1);
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var accounts = Snapshot(Envir.Main.AccountList).Where(a => a != null);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.Trim();
                    accounts = accounts.Where(a =>
                        a.AccountID.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (a.UserName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (a.LastIP ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                if (banned.HasValue) accounts = accounts.Where(a => a.Banned == banned.Value);

                var list = accounts.OrderBy(a => a.Index).ToArray();
                var items = list.Skip((currentPage - 1) * size).Take(size).Select(a => new
                {
                    index = a.Index,
                    accountId = a.AccountID,
                    userName = a.UserName,
                    banned = a.Banned,
                    banReason = a.BanReason,
                    expiryDate = a.ExpiryDate,
                    lastIp = a.LastIP,
                    lastDate = a.LastDate,
                    admin = a.AdminAccount,
                    requirePasswordChange = a.RequirePasswordChange,
                    characterCount = a.Characters?.Count ?? 0
                }).ToArray();

                return Results.Ok(new { total = list.Length, page = currentPage, pageSize = size, items });
            });

            api.MapGet("/accounts/{index:int}", (int index) =>
            {
                var account = FindAccount(index);
                if (account == null) return Results.NotFound(new { message = "账号不存在" });

                return Results.Ok(new
                {
                    index = account.Index,
                    accountId = account.AccountID,
                    userName = account.UserName,
                    email = account.EMailAddress,
                    banned = account.Banned,
                    banReason = account.BanReason,
                    expiryDate = account.ExpiryDate,
                    creationIp = account.CreationIP,
                    creationDate = account.CreationDate,
                    lastIp = account.LastIP,
                    lastDate = account.LastDate,
                    admin = account.AdminAccount,
                    requirePasswordChange = account.RequirePasswordChange,
                    hasStoragePassword = account.HasStoragePassword,
                    gold = account.Gold,
                    credit = account.Credit,
                    characters = (account.Characters ?? new List<CharacterInfo>()).Select(c => new
                    {
                        index = c.Index,
                        name = c.Name,
                        level = c.Level,
                        className = c.Class.ToString()
                    }).ToArray()
                });
            });

            api.MapPost("/accounts/{index:int}/ban", (int index, BanRequest request) =>
            {
                var account = FindAccount(index);
                if (account == null) return Results.NotFound(new { message = "账号不存在" });

                account.Banned = true;
                account.BanReason = request.Reason ?? string.Empty;
                account.ExpiryDate = request.Days > 0 ? DateTime.Now.AddDays(request.Days) : DateTime.MaxValue;
                account.Connection?.SendDisconnect(4);
                Envir.Main.SaveAccounts();
                WebLog.Info($"Account {account.AccountID} banned. Reason={account.BanReason}, Expiry={account.ExpiryDate:O}");
                return Results.Ok(new { ok = true, message = "账号已封禁" });
            });

            api.MapPost("/accounts/{index:int}/unban", (int index) =>
            {
                var account = FindAccount(index);
                if (account == null) return Results.NotFound(new { message = "账号不存在" });

                account.Banned = false;
                account.BanReason = string.Empty;
                account.ExpiryDate = DateTime.MinValue;
                Envir.Main.SaveAccounts();
                WebLog.Info($"Account {account.AccountID} unbanned");
                return Results.Ok(new { ok = true, message = "账号已解封" });
            });

            api.MapPost("/accounts/{index:int}/password", (int index, PasswordRequest request) =>
            {
                var account = FindAccount(index);
                if (account == null) return Results.NotFound(new { message = "账号不存在" });
                if (!Envir.IsPasswordValid(request.Password))
                    return Results.BadRequest(new { message = $"密码长度必须为 {Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位，仅限字母和数字" });

                account.Password = request.Password;
                account.RequirePasswordChange = true;
                account.Connection?.SendDisconnect(4);
                Envir.Main.SaveAccounts();
                WebLog.Info($"Account {account.AccountID} password reset from web admin");
                return Results.Ok(new { ok = true, message = "密码已重置，玩家下次登录需要修改密码" });
            });

            api.MapDelete("/accounts/{index:int}", (int index) =>
            {
                var account = FindAccount(index);
                if (account == null) return Results.NotFound(new { message = "账号不存在" });
                if (account.AdminAccount) return Results.BadRequest(new { message = "不能删除管理员账号" });

                account.Connection?.SendDisconnect(4);
                Envir.Main.AccountList.Remove(account);
                Envir.Main.SaveAccounts();
                WebLog.Warn($"Account {account.AccountID} deleted from web admin");
                return Results.Ok(new { ok = true, message = "账号已删除" });
            });

            api.MapGet("/players", () =>
            {
                var players = Snapshot(Envir.Main.Players).Where(p => p != null).Select(p => new
                {
                    name = p.Name,
                    accountId = p.Account?.AccountID,
                    level = p.Info?.Level ?? 0,
                    className = p.Class.ToString(),
                    map = p.CurrentMap?.Info?.FileName,
                    x = p.CurrentLocation.X,
                    y = p.CurrentLocation.Y,
                    hp = p.HP,
                    mp = p.MP,
                    ip = p.Connection?.IPAddress,
                    isGm = p.IsGM,
                    dead = p.Dead
                }).ToArray();

                return Results.Ok(players);
            });

            api.MapPost("/players/{name}/kick", (string name) =>
            {
                var player = FindPlayer(name);
                if (player == null) return Results.NotFound(new { message = "玩家不在线" });

                player.Connection?.SendDisconnect(4);
                WebLog.Info($"Player {player.Name} kicked from web admin");
                return Results.Ok(new { ok = true, message = "玩家已踢下线" });
            });

            api.MapPost("/players/message", (MessageRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                    return Results.BadRequest(new { message = "消息不能为空" });

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    Envir.Main.Broadcast(new S.Chat { Message = request.Message, Type = ChatType.Hint });
                    WebLog.Info($"Broadcast from web admin: {request.Message}");
                }
                else
                {
                    var player = FindPlayer(request.Name);
                    if (player == null) return Results.NotFound(new { message = "玩家不在线" });
                    player.ReceiveChat(request.Message, ChatType.Hint);
                    WebLog.Info($"Message to {player.Name} from web admin: {request.Message}");
                }

                return Results.Ok(new { ok = true, message = "消息已发送" });
            });

            api.MapGet("/logs", (string type, int? lines) =>
            {
                var folder = (type ?? "server").ToLowerInvariant() switch
                {
                    "server" => "Server",
                    "chat" => "Chat",
                    "player" => "Player",
                    "debug" => "Debug",
                    "spawn" => "Spawn",
                    _ => null
                };
                if (folder == null) return Results.BadRequest(new { message = "未知日志类型" });

                var dir = Path.Combine("Logs", folder);
                if (!Directory.Exists(dir)) return Results.Ok(new { file = (string)null, lines = Array.Empty<string>() });

                var file = Directory.EnumerateFiles(dir, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (file == null) return Results.Ok(new { file = (string)null, lines = Array.Empty<string>() });

                return Results.Ok(new
                {
                    file = Path.GetFileName(file),
                    lines = TailLines(file, Math.Clamp(lines ?? 200, 1, 2000))
                });
            });

            api.MapGet("/db/summary", () =>
            {
                var envir = Envir.Main;
                return Results.Ok(new
                {
                    items = envir.ItemInfoList.Count,
                    monsters = envir.MonsterInfoList.Count,
                    maps = envir.MapInfoList.Count,
                    npcs = envir.NPCInfoList.Count,
                    quests = envir.QuestInfoList.Count,
                    recipes = envir.RecipeInfoList.Count,
                    buffs = envir.BuffInfoList.Count,
                    guilds = envir.GuildList.Count,
                    accounts = envir.AccountList.Count
                });
            });

            api.MapGet("/db/items", (string q, int? page, int? pageSize) =>
            {
                var currentPage = Math.Max(1, page ?? 1);
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var source = Snapshot(Envir.Main.ItemInfoList).Where(i => i != null);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.Trim();
                    source = source.Where(i =>
                        i.Index.ToString().Contains(q) ||
                        (i.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                var list = source.OrderBy(i => i.Index).ToArray();
                var items = list.Skip((currentPage - 1) * size).Take(size).Select(i => new
                {
                    index = i.Index,
                    name = i.Name,
                    type = i.Type.ToString(),
                    grade = i.Grade.ToString(),
                    price = i.Price
                }).ToArray();

                return Results.Ok(new { total = list.Length, page = currentPage, pageSize = size, items });
            });

            api.MapGet("/db/monsters", (string q, int? page, int? pageSize) =>
            {
                var currentPage = Math.Max(1, page ?? 1);
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var source = Snapshot(Envir.Main.MonsterInfoList).Where(m => m != null);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.Trim();
                    source = source.Where(m =>
                        m.Index.ToString().Contains(q) ||
                        (m.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (m.GameName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                var list = source.OrderBy(m => m.Index).ToArray();
                var items = list.Skip((currentPage - 1) * size).Take(size).Select(m => new
                {
                    index = m.Index,
                    name = m.Name,
                    gameName = m.GameName,
                    level = m.Level
                }).ToArray();

                return Results.Ok(new { total = list.Length, page = currentPage, pageSize = size, items });
            });

            api.MapGet("/db/maps", (string q, int? page, int? pageSize) =>
            {
                var currentPage = Math.Max(1, page ?? 1);
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                IEnumerable<MapInfo> source = Snapshot(Envir.Main.MapInfoList);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.Trim();
                    source = source.Where(m =>
                        (m.FileName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (m.Title ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                var list = source.OrderBy(m => m.Index).ToArray();
                var items = list.Skip((currentPage - 1) * size).Take(size).Select(m => new
                {
                    index = m.Index,
                    fileName = m.FileName,
                    title = m.Title
                }).ToArray();

                return Results.Ok(new { total = list.Length, page = currentPage, pageSize = size, items });
            });

            api.MapGet("/db/npcs", (string q, int? page, int? pageSize) =>
            {
                var currentPage = Math.Max(1, page ?? 1);
                var size = Math.Clamp(pageSize ?? 25, 1, 100);
                var source = Snapshot(Envir.Main.NPCInfoList).Where(n => n != null);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.Trim();
                    source = source.Where(n =>
                        (n.FileName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (n.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                var list = source.OrderBy(n => n.Index).ToArray();
                var items = list.Skip((currentPage - 1) * size).Take(size).Select(n => new
                {
                    index = n.Index,
                    fileName = n.FileName,
                    name = n.Name,
                    mapIndex = n.MapIndex
                }).ToArray();

                return Results.Ok(new { total = list.Length, page = currentPage, pageSize = size, items });
            });

            api.MapGet("/config", () => Results.Ok(new
            {
                general = new
                {
                    language = Settings.Language,
                    multithreaded = Settings.Multithreaded,
                    threadLimit = Settings.ThreadLimit,
                    saveDelay = Settings.SaveDelay,
                    testServer = Settings.TestServer,
                    checkVersion = Settings.CheckVersion
                },
                network = new
                {
                    ipAddress = Settings.IPAddress,
                    port = Settings.Port,
                    maxUser = Settings.MaxUser,
                    maxIp = Settings.MaxIP,
                    timeout = Settings.TimeOut,
                    startHttpService = Settings.StartHTTPService
                },
                paths = new
                {
                    envir = Settings.EnvirPath,
                    maps = Settings.MapPath,
                    configs = Settings.ConfigPath
                }
            }));

            api.MapPost("/admin/password", (AdminPasswordRequest request, WebAdminSettings web) =>
            {
                if (!web.Verify(web.Username, request.CurrentPassword))
                    return Results.BadRequest(new { message = "当前密码错误" });
                if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                    return Results.BadRequest(new { message = "新密码至少 8 位" });
                if (request.NewPassword == WebAdminSettings.DefaultPassword && web.MustChangePassword)
                    return Results.BadRequest(new { message = "请不要继续使用默认密码" });

                web.SetPassword(request.NewPassword, false);
                web.Save();
                WebLog.Info("Web admin password changed");
                return Results.Ok(new { ok = true, message = "管理员密码已修改" });
            });
        }

        private static AccountInfo FindAccount(int index)
        {
            return Snapshot(Envir.Main.AccountList).FirstOrDefault(a => a != null && a.Index == index);
        }

        private static PlayerObject FindPlayer(string name)
        {
            return Snapshot(Envir.Main.Players).FirstOrDefault(p =>
                p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static T[] Snapshot<T>(List<T> list)
        {
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    return list.ToArray();
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(5);
                }
            }

            return Array.Empty<T>();
        }

        private static string[] TailLines(string path, int count)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var queue = new Queue<string>(count);

            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == count) queue.Dequeue();
                queue.Enqueue(line);
            }

            return queue.ToArray();
        }
    }
}
