# BIIS на C# через интернет — инструкция для Windows

## Постановка задачи

Добавить в BIS.ERP двусторонний обмен между площадками A и B через интернет. На каждой площадке работает фоновый процесс Windows, который принимает разрешённые команды и выполняет их в своей базе через прикладной слой BIIS. Соединение восстанавливается после обрыва. Повтор команды записи с тем же идентификатором возвращает сохранённый результат. Используются собственные компоненты BIIS, .NET и штатные средства Windows.

## 1. Выбрать, кто слушает интернет

- Если A доступна извне по TCP 443, разместите приёмник WSS в службе A. B подключается к A. Канал двусторонний.
- Если входящие подключения закрыты у обеих площадок, разверните собственный BIIS Relay на управляемом Windows-сервере с публичным адресом. A и B подключаются к нему исходящими соединениями. Дальнейшие шаги рассчитаны на эту схему.
- Если публичного адреса нет ни у одной площадки и отдельного сервера тоже нет, сначала обеспечьте доступную точку. Проброс на домашнем маршрутизаторе сам по себе не решает CGNAT.

    WPF A → служба A ⇄ WSS ⇄ BIIS Relay ⇄ WSS ⇄ служба B → база B

## 2. Создать проекты внутри репозитория

Корень Git и каталог WPF-проекта совпадают: D:/C#/BIS.ERP/BIS.ERP. В BIS.ERP.csproj допишите ;Peer\** в конец существующего DefaultItemExcludes, чтобы WPF пропускал исходники вложенных проектов.

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);BIS.ERP.TESTS\**;BIS.ERP.Updater\**;Tests\**;Peer\**</DefaultItemExcludes>
```

Выполните из корня репозитория в Git Bash:

```bash
dotnet new worker -n BIS.ERP.PeerHost -o Peer/BIS.ERP.PeerHost -f net8.0
dotnet new web -n BIS.ERP.Relay -o Peer/BIS.ERP.Relay -f net8.0
dotnet new classlib -n BIS.ERP.PeerProtocol -o Peer/BIS.ERP.PeerProtocol -f net8.0
dotnet add Peer/BIS.ERP.PeerHost/BIS.ERP.PeerHost.csproj reference Peer/BIS.ERP.PeerProtocol/BIS.ERP.PeerProtocol.csproj
dotnet add Peer/BIS.ERP.Relay/BIS.ERP.Relay.csproj reference Peer/BIS.ERP.PeerProtocol/BIS.ERP.PeerProtocol.csproj
git status --short --untracked-files=all -- Peer BIS.ERP.csproj
```

В Visual Studio добавьте три файла .csproj из Peer через «Добавить → Существующий проект» на уровне решения. Соберите решение. В PeerProtocol положите DTO запросов, ответов и служебных сообщений. В обоих исполняемых проектах подключите Microsoft.Extensions.Hosting.WindowsServices версии, совместимой с .NET 8, и зарегистрируйте AddWindowsService. Узел реализуйте как BackgroundService.

## 3. Собрать проверку связи без базы

1. В Relay включите UseWebSockets и создайте маршрут /peer. Держите обработчик запроса активным до закрытия сокета. Храните таблицу активных сессий по ID узла. При замене сессии закрывайте прежнюю и при её завершении удаляйте только её собственную запись.
2. В PeerHost подключайтесь через ClientWebSocket к wss://relay.example.com/peer. Для начала сделайте ping, который проходит через Relay к другому узлу, и pong с тем же requestId.
3. Добавьте поля version, type, requestId, target, operation, deadlineUtc и payload. Для ответа предусмотрите статус и результат. Один цикл читает сокет, одна ограниченная очередь сериализует отправку. Собирайте фрагменты до EndOfMessage и ограничивайте общий размер сообщения. При отсутствии адресата возвращайте peer_unavailable.
4. Проверьте A → B и B → A. Добавьте тайм-аут ответа, heartbeat с ожиданием подтверждения и переподключение с нарастающей задержкой и случайным разбросом. После разрыва создавайте новый ClientWebSocket.

## 4. Настроить сертификаты и внешний адрес

1. Создайте DNS-запись публичной точки. Настройте Kestrel на HTTPS/TCP 443 с серверным сертификатом, имя которого совпадает с DNS. Для закрытого теста можно использовать собственный УЦ и установить его доверенный корень на A и B. Используйте отдельные клиентские сертификаты A и B.
2. В Kestrel потребуйте клиентский сертификат. Проверяйте цепочку, срок и принадлежность сертификата конкретному узлу. На Relay задайте допустимую пару A/B и вычисляйте отправителя из сессии. На PeerHost сохраняйте проверку цепочки и имени сервера.
3. Дайте учётной записи службы доступ к её закрытому ключу. Откройте входящий TCP 443 на Relay. Если сервер за маршрутизатором с публичным адресом, настройте проброс. На A и B разрешите исходящий TCP 443. С каждой площадки выполните:

```powershell
Test-NetConnection relay.example.com -Port 443
```

Успешная проверка TCP подтверждает доступность порта. TLS, сертификаты и маршрут проверяются отдельным WSS-подключением. Relay завершает TLS на каждом участке и имеет доступ к телам сообщений.

## 5. Подключить чтение из BIIS и вызов из WPF

1. Выберите одну операцию, например чтение карточки справочника по ID. Проверьте зависимости AppDbContext и выбранного сервиса. Вынесите минимальный переиспользуемый слой в библиотеку, если он сейчас связан с WPF или интерактивной сессией. Создавайте отдельный контекст БД на запрос.
2. В PeerHost задайте целевую инфобазу и отдельные учётные данные службы. На принимающей стороне проверяйте разрешение пары «узел + операция» и входные параметры. Запускайте обработчик из явного списка. База остаётся внутри площадки.
3. Для вызова WPF → локальный PeerHost используйте именованный канал Windows с ACL для разрешённых учётных записей. В WPF проверьте права пользователя перед отправкой, в службе ограничьте доступ к каналу и разрешённым операциям. Сначала выполните вызов из локальной тестовой консоли, затем подключите кнопку в WPF. Операция на B должна работать при закрытом WPF B.

## 6. Добавить одну операцию записи

Передавайте commandId, который сохраняется при повторах. В базе получателя создайте журнал команд с уникальным ключом (senderId, commandId), хешем нормализованного запроса и результатом. Записывайте результат и изменение данных в одной транзакции. Обработайте параллельные дубли через уникальный ключ. Повтор с другим содержимым возвращает конфликт. При потере ответа повторяйте с прежним commandId. Если команда должна переживать перезапуск отправителя, сохраняйте её в локальной очереди до подтверждения.

## 7. Опубликовать и зарегистрировать службы

После проверки консольного запуска опубликуйте из корня репозитория. Пример для x64:

```powershell
dotnet publish .\Peer\BIS.ERP.PeerHost -c Release -r win-x64 --self-contained true -o C:\BIIS\PeerHost
dotnet publish .\Peer\BIS.ERP.Relay -c Release -r win-x64 --self-contained true -o C:\BIIS\Relay
```

Выполните на соответствующих компьютерах в PowerShell администратора. Подставьте подготовленную учётную запись с доступом к конфигурации, сертификату и, для PeerHost, базе. Предоставьте ей право «Вход в качестве службы».

```powershell
$biisServiceCredential = Get-Credential
# На A и B
New-Service -Name BIISPeerHost -BinaryPathName 'C:\BIIS\PeerHost\BIS.ERP.PeerHost.exe' -StartupType Automatic -Credential $biisServiceCredential
Start-Service BIISPeerHost
# На сервере Relay, с его учётной записью
New-Service -Name BIISRelay -BinaryPathName 'C:\BIIS\Relay\BIS.ERP.Relay.exe' -StartupType Automatic -Credential $biisServiceCredential
Start-Service BIISRelay
```

Используйте каталог приложения или абсолютные пути для настроек. Настройте перезапуск при сбое на вкладке «Восстановление» в свойствах службы. Проверьте журнал Windows и логируйте ID запроса, операцию, статус и длительность без полезной нагрузки и секретов.

## 8. Проверить готовность

- A и B находятся в разных интернет-сетях. Чтение и запись проходят в обе стороны.
- После перезагрузки Windows службы поднимаются автоматически. Закрытие WPF не влияет на приём команд.
- Недоверенный сертификат и запрещённая операция отклоняются.
- После отключения интернета и перезапуска Relay соединения восстанавливаются.
- Если оборвать связь после фиксации записи и повторить команду, в базе остаётся одно изменение.
- При выключенном получателе отправитель получает ошибку недоступности или тайм-аут в установленный срок.

## Справка Microsoft

- [Запуск ASP.NET Core как службы Windows](https://learn.microsoft.com/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-8.0)
- [WebSockets в ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/websockets?view=aspnetcore-8.0)


## Запуск реализованного локального ping/pong

Из корня репозитория откройте три окна Git Bash и выполните по одной команде в каждом:

```bash
dotnet run --project Peer/BIS.ERP.Relay --no-launch-profile
```

```bash
dotnet run --project Peer/BIS.ERP.PeerHost --no-launch-profile -- --Peer:NodeId=A
```

```bash
dotnet run --project Peer/BIS.ERP.PeerHost --no-launch-profile -- --Peer:NodeId=B
```

Узлы каждые 5 секунд отправляют ping друг другу и записывают в консоль полученный pong с ID запроса. Если второй узел ещё не подключён, появляется unavailable. После запуска обоих появляются ответы pong. Остановка выполняется Ctrl+C. При перезапуске Relay узлы повторяют подключение каждые 3 секунды. Ожидание ответа ограничено 15 секундами.

Текущая реализация допускает только локальный ws на 127.0.0.1:5080 и ID A/B для проверки. Для работы через интернет следующим шагом реализуются WSS, аутентификация сертификатами и авторизация узлов.

## Практическое выполнение пункта 3

Ниже приведён полный код добавленного локального обмена. Этот раздел выполняется после создания проектов из пункта 2. Все пути отсчитываются от D:/C#/BIS.ERP/BIS.ERP. В Visual Studio открывайте файл двойным щелчком и заменяйте его содержимое целиком. Перед изменением сохраняйте текущую работу в Git.

### Шаг 1. Проверить проекты и ссылки

В Git Bash из корня репозитория выполните команды. Они подключают общую библиотеку к узлу и Relay.

```bash
dotnet add Peer/BIS.ERP.PeerHost/BIS.ERP.PeerHost.csproj reference Peer/BIS.ERP.PeerProtocol/BIS.ERP.PeerProtocol.csproj
dotnet add Peer/BIS.ERP.Relay/BIS.ERP.Relay.csproj reference Peer/BIS.ERP.PeerProtocol/BIS.ERP.PeerProtocol.csproj
```

Проверьте, что в BIS.ERP.csproj уже есть исключение Peer из пункта 2. Ниже приведены текущие файлы проектов для сверки. UserSecretsId у вас может отличаться.

Файл Peer/BIS.ERP.PeerProtocol/BIS.ERP.PeerProtocol.csproj.

~~~xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
~~~

Файл Peer/BIS.ERP.PeerHost/BIS.ERP.PeerHost.csproj.

~~~xml
<Project Sdk="Microsoft.NET.Sdk.Worker">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>dotnet-BIS.ERP.PeerHost-4b57f63a-23f1-4f65-851b-64e9edacc20a</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BIS.ERP.PeerProtocol\BIS.ERP.PeerProtocol.csproj" />
  </ItemGroup>
</Project>
~~~

Файл Peer/BIS.ERP.Relay/BIS.ERP.Relay.csproj.

~~~xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\BIS.ERP.PeerProtocol\BIS.ERP.PeerProtocol.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
~~~

### Шаг 2. Создать общие сообщения

В проекте BIS.ERP.PeerProtocol добавьте класс PeerMessage.cs через «Добавить → Класс». Вставьте следующий код целиком. Шаблонный Class1.cs можно оставить пустым.

PeerMessage описывает сообщение, а PeerWire сериализует его в JSON и передаёт через WebSocket. Чтение объединяет фрагменты одного сообщения и ограничивает его размер 16 КБ. RequestId связывает ping с ответом, Target определяет получателя, Sender назначается Relay.

Файл Peer/BIS.ERP.PeerProtocol/PeerMessage.cs.

~~~csharp
using System.Net.WebSockets;
using System.Text.Json;

namespace BIS.ERP.PeerProtocol;

public sealed record PeerMessage(string Type, Guid RequestId, string Target, string? Sender = null);

public static class PeerWire
{
    public static async Task SendAsync(WebSocket socket, PeerMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token);
    }

    public static async Task<PeerMessage?> ReceiveAsync(WebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > 16384)
                throw new InvalidDataException("Expected a text message of at most 16384 bytes.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var message = JsonSerializer.Deserialize<PeerMessage>(stream.ToArray())
            ?? throw new InvalidDataException("Empty message.");
        if (message.RequestId == Guid.Empty || message.Type is not ("ping" or "pong" or "unavailable")
            || message.Target is not ("A" or "B"))
            throw new InvalidDataException("Invalid peer message.");
        return message;
    }
}
~~~

### Шаг 3. Настроить Relay

В проекте BIS.ERP.Relay откройте Program.cs и замените содержимое следующим кодом. Relay слушает 127.0.0.1:5080, принимает подключения A и B на /peer, назначает отправителя из сессии и пересылает сообщение получателю. Если получатель отсутствует, отправитель получает unavailable. Повторное подключение с уже занятым ID отклоняется.

Файл Peer/BIS.ERP.Relay/Program.cs.

~~~csharp
using System.Collections.Concurrent;
using System.Net.WebSockets;
using BIS.ERP.PeerProtocol;

var builder = WebApplication.CreateBuilder(args);
// This unauthenticated test endpoint is deliberately limited to loopback.
builder.WebHost.UseUrls("http://127.0.0.1:5080");
var app = builder.Build();
var sessions = new ConcurrentDictionary<string, RelaySession>();
app.UseWebSockets();
app.Map("/peer", async context =>
{
    var id = context.Request.Query["node"].ToString();
    if (!context.WebSockets.IsWebSocketRequest || id is not ("A" or "B"))
    {
        context.Response.StatusCode = 400;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var session = new RelaySession(socket);
    if (!sessions.TryAdd(id, session))
    {
        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Node already connected", context.RequestAborted);
        return;
    }
    app.Logger.LogInformation("Node {Node} connected", id);
    try
    {
        while (await PeerWire.ReceiveAsync(socket, context.RequestAborted) is { } message)
        {
            if (message.Target == id)
                throw new InvalidDataException("Target must be the other node.");
            if (sessions.TryGetValue(message.Target, out var target))
            {
                await target.SendAsync(message with { Sender = id }, context.RequestAborted);
            }
            else
            {
                await session.SendAsync(new PeerMessage("unavailable", message.RequestId, id), context.RequestAborted);
            }
        }
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed", context.RequestAborted);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
    {
        app.Logger.LogInformation("Node {Node} disconnected: {Reason}", id, ex.Message);
    }
    finally
    {
        sessions.TryRemove(id, out _);
    }
});
app.Run();

sealed class RelaySession(WebSocket socket)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(PeerMessage message, CancellationToken token)
    {
        await _sendLock.WaitAsync(token);
        try { await PeerWire.SendAsync(socket, message, token); }
        finally { _sendLock.Release(); }
    }
}
~~~

### Шаг 4. Подключить фоновый обработчик

В проекте BIS.ERP.PeerHost откройте Program.cs. Этот код регистрирует Worker как фоновую задачу и запускает приложение.

Файл Peer/BIS.ERP.PeerHost/Program.cs.

~~~csharp
using BIS.ERP.PeerHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
~~~

### Шаг 5. Добавить работу узла

В том же проекте замените Worker.cs следующим кодом. Один и тот же процесс запускается с NodeId=A или NodeId=B. Он отправляет ping каждые 5 секунд, отвечает pong, сопоставляет ответы по RequestId и подключается заново после обрыва. SemaphoreSlim последовательно выполняет отправки в один сокет.

Файл Peer/BIS.ERP.PeerHost/Worker.cs.

~~~csharp
using System.Net.WebSockets;
using BIS.ERP.PeerProtocol;

namespace BIS.ERP.PeerHost;

public sealed class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var node = configuration["Peer:NodeId"] ?? "A";
        if (node is not ("A" or "B"))
            throw new InvalidOperationException("Peer:NodeId must be A or B.");
        var target = node == "A" ? "B" : "A";
        var relay = new Uri(configuration["Peer:RelayUrl"] ?? "ws://127.0.0.1:5080/peer");
        if (relay.Scheme != "ws" || !relay.IsLoopback)
            throw new InvalidOperationException("This test supports only loopback ws connections.");
        while (!stoppingToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using var sendLock = new SemaphoreSlim(1, 1);
            Task? receiver = null;
            try
            {
                await socket.ConnectAsync(new UriBuilder(relay) { Query = $"node={node}" }.Uri, stoppingToken);
                logger.LogInformation("Node {Node} connected", node);

                async Task SendAsync(PeerMessage message)
                {
                    await sendLock.WaitAsync(session.Token);
                    try { await PeerWire.SendAsync(socket, message, session.Token); }
                    finally { sendLock.Release(); }
                }
                var pending = new Dictionary<Guid, DateTimeOffset>();
                var pendingLock = new object();
                async Task ReceiveAsync()
                {
                    try
                    {
                        while (await PeerWire.ReceiveAsync(socket, session.Token) is { } message)
                        {
                            if (message.Target != node)
                                throw new InvalidDataException("Unexpected target.");
                            if (message.Type == "ping" && message.Sender == target)
                            {
                                await SendAsync(new PeerMessage("pong", message.RequestId, target));
                                logger.LogInformation("Ping received, pong sent {RequestId}", message.RequestId);
                            }
                            else
                            {
                                bool matched;
                                lock (pendingLock) matched = pending.Remove(message.RequestId);
                                if (matched)
                                    logger.LogInformation("{Type} received {RequestId}", message.Type, message.RequestId);
                            }
                        }
                    }
                    finally { session.Cancel(); }
                }
                receiver = ReceiveAsync();
                while (!session.IsCancellationRequested)
                {
                    lock (pendingLock)
                    {
                        if (pending.Values.Any(time => DateTimeOffset.UtcNow - time > TimeSpan.FromSeconds(15)))
                            throw new TimeoutException("Peer response timed out.");
                    }
                    var id = Guid.NewGuid();
                    lock (pendingLock) pending.Add(id, DateTimeOffset.UtcNow);
                    await SendAsync(new PeerMessage("ping", id, target));
                    logger.LogInformation("Ping sent to {Target} {RequestId}", target, id);
                    await Task.Delay(TimeSpan.FromSeconds(5), session.Token);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or TimeoutException or InvalidDataException or System.Text.Json.JsonException)
            {
                logger.LogInformation("Connection ended: {Reason}", ex.Message);
            }
            finally
            {
                session.Cancel();
                socket.Abort();
                if (receiver is not null)
                {
                    try { await receiver; }
                    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
                    {
                        logger.LogDebug("Receive loop ended: {Reason}", ex.Message);
                    }
                }
            }
            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
~~~

### Шаг 6. Проверить конфигурацию

Адрес Relay по умолчанию задан в Worker.cs как ws://127.0.0.1:5080/peer. ID узла передаётся аргументом при запуске. Для этого прогона appsettings.json может остаться таким, как в шаблоне.

Файл Peer/BIS.ERP.PeerHost/appsettings.json.

~~~json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
~~~

### Шаг 7. Собрать проекты

В Git Bash из корня репозитория выполните обе команды. Каждая должна завершиться с нулём ошибок. PeerProtocol соберётся автоматически как зависимость.

```bash
dotnet build Peer/BIS.ERP.Relay/BIS.ERP.Relay.csproj
dotnet build Peer/BIS.ERP.PeerHost/BIS.ERP.PeerHost.csproj
```

### Шаг 8. Запустить три процесса

Откройте три окна Git Bash в корне репозитория. Сначала запустите Relay в первом окне.

```bash
dotnet run --project Peer/BIS.ERP.Relay --no-launch-profile
```

Во втором окне запустите узел A.

```bash
dotnet run --project Peer/BIS.ERP.PeerHost --no-launch-profile -- --Peer:NodeId=A
```

В третьем окне запустите узел B.

```bash
dotnet run --project Peer/BIS.ERP.PeerHost --no-launch-profile -- --Peer:NodeId=B
```

--no-launch-profile позволяет запускать процессы с указанными параметрами без настроек профиля Visual Studio.

### Шаг 9. Проверить результат и переподключение

В Relay должны появиться Node A connected и Node B connected. У каждого узла появятся строки следующего вида.

```text
Node A connected
Ping sent to B be068dba-73f8-4011-a058-faa7e56f8637
pong received be068dba-73f8-4011-a058-faa7e56f8637
```

ID в паре отправки и ответа совпадают. Пока B отсутствует, A получает unavailable. Остановите Relay через Ctrl+C, затем запустите той же командой. Оба узла должны переподключиться, после чего обмен возобновится. Завершите три процесса через Ctrl+C.

Если порт 5080 занят, остановите ранее запущенный экземпляр Relay. Если появляется Node already connected, проверьте, что запущен только один A и один B.

### Шаг 10. Перейти к интернету

Этим кодом выполнена локальная проверка пункта 3. Перед размещением публичной точки выполните пункт 4. Потребуется настроить WSS и клиентские сертификаты, получать ID узла из проверенной идентичности и изменить ограничение локального подключения в Worker. Затем повторите ping/pong между двумя площадками. Следующие этапы документа предусматривают прикладные операции с базой и запуск в качестве службы Windows.

## Развёртывание между подсетями и через интернет

Раздел дополнен 4 ноября 2026 года. Он продолжает проверенный обмен ping/pong. Папки Peer содержат локальный прототип. Настройки и изменения кода ниже необходимо выполнить перед сетевым развёртыванием.

### 1. Выбрать схему размещения

Relay принимает соединения и пересылает сообщения. На каждом участке TLS заканчивается на Relay, поэтому его администратор имеет доступ к сообщениям. Узлы A и B сами подключаются к Relay и могут отправлять запросы в обе стороны.

| Условия | Где размещать Relay | Что требуется |
|---|---|---|
| Разные подсети одной организации | Сервер в любой доступной подсети | Маршруты и разрешение TCP к Relay |
| Интернет, публичный IPv4 на домашнем роутере | Домашний Windows-сервер | Проброс TCP-порта и сертификаты |
| Интернет, CGNAT дома | Домашний сервер после изменения доступа | Публичный IPv4 от провайдера либо глобальный IPv6 |
| Интернет, глобальный IPv6 | Сервер с глобальным IPv6 | Доступный IPv6 у A и B, правило входящего доступа |
| Входящие соединения недоступны везде | Сначала обеспечить публичную точку | В данной схеме отсутствует адрес для подключения |

Для обоих сценариев далее используйте один защищённый порт TCP 5443. При необходимости можно выбрать 443, проверив, что он свободен. Все настройки должны использовать одинаковый выбранный порт.

### 2. Локальная сеть с разными подсетями

Пример адресов следует заменить фактическими адресами вашей организации.

| Устройство | IP и маска | Шлюз |
|---|---|---|
| A | 192.168.10.15/24 | 192.168.10.1 |
| B | 192.168.20.25/24 | 192.168.20.1 |
| Relay | 192.168.30.10/24 | 192.168.30.1 |

Подсети должны быть связаны маршрутизатором или L3-коммутатором. В примере он знает три сети. Разные номера подсетей сами по себе связь не создают.

1. На сервере закрепите IP Relay статической настройкой или DHCP reservation.
2. На сетевом оборудовании проверьте маршруты к 192.168.10.0/24, 192.168.20.0/24 и 192.168.30.0/24. При нескольких маршрутизаторах нужны маршруты туда и обратно. Если маршрутизацию обслуживает администратор, передайте ему таблицу выше и запрос доступа A/B → 192.168.30.10 TCP 5443.
3. Для изолированных VLAN разрешите на межсетевом экране новые TCP-соединения из сетей A и B к Relay и ответный трафик этих соединений. Прямое соединение A → B для Relay не требуется.
4. Проверьте на Windows вывод ipconfig /all и route print. Шлюз должен вести к маршрутизатору своей подсети. Если сервер имеет несколько интерфейсов, проверьте, через какой из них возвращается ответ.
5. Создайте во внутреннем DNS имя biis-relay.corp.example, указывающее на 192.168.30.10. Имя в сертификате и адресе WSS должно совпадать. Для небольшого теста можно добавить строку 192.168.30.10 biis-relay.corp.example в файл C:/Windows/System32/drivers/etc/hosts на A и B с правами администратора.
6. Выполните настройку сертификатов и кода из следующих разделов.
7. На сервере откройте PowerShell администратора и разрешите входящий порт только от нужных сетей.

```powershell
New-NetFirewallRule -DisplayName "BIIS Relay LAN 5443" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5443 -RemoteAddress 192.168.10.0/24,192.168.20.0/24
```

8. После запуска Relay проверьте на нём наличие слушателя, затем проверьте TCP с A и B.

```powershell
# На сервере Relay
Get-NetTCPConnection -LocalPort 5443 -State Listen

# На A и B
Resolve-DnsName biis-relay.corp.example
Test-NetConnection biis-relay.corp.example -Port 5443
```

9. Запустите PeerHost с NodeId=A и NodeId=B и WSS-адресом из раздела конфигурации. Дождитесь pong в обе стороны.
10. Перезапустите Relay, затем проверьте восстановление обмена. Закройте WPF на принимающей стороне и убедитесь, что отдельно запущенный PeerHost продолжает отвечать.

Проверка ping утилитой Windows относится к ICMP. Его блокировка сама по себе не доказывает недоступность TCP 5443. Успешный Test-NetConnection проверяет TCP, затем необходима отдельная проверка WSS и клиентских сертификатов.

### 3. Подготовить сертификаты

Для собственной инфраструктуры используйте собственный удостоверяющий центр и сертификаты, выданные им. На начальном этапе можно воспользоваться внутренним УЦ организации. Сертификат Relay должен иметь назначение Server Authentication и DNS-имя выбранного адреса. Сертификаты A и B должны иметь назначение Client Authentication и разные ключи.

Последовательность подготовки:

1. Выпустите серверный сертификат для biis-relay.corp.example в локальной сети либо для вашего публичного имени в интернете. При смене имени выпустите сертификат с новым именем или с обоими DNS-именами в SAN.
2. Выпустите отдельные клиентские сертификаты A и B. Корневой сертификат УЦ установите в доверенные корневые центры на Relay, A и B. Промежуточные сертификаты установите в соответствующее хранилище.
3. Клиентские сертификаты с закрытым ключом установите на соответствующий компьютер в LocalMachine/My. Серверный сертификат с ключом установите на Relay в LocalMachine/My.
4. Через certlm.msc откройте «Личное → Сертификаты». Запишите отпечатки сертификатов. В примерах ниже используется свойство Thumbprint из Windows.
5. В меню сертификата выберите «Все задачи → Управление закрытыми ключами» и разрешите чтение ключа учётной записи соответствующей службы. Для консольного запуска доступ должен иметь текущий пользователь.
6. Убедитесь, что часы синхронизированы и все компьютеры могут проверить цепочку и отзыв сертификатов. При собственном УЦ предусмотрите доступность его CRL.
7. При замене сертификата обновляйте отпечаток в конфигурации. Для эксплуатации заранее предусмотрите согласованное окно смены сертификатов.

Закрытые ключи и пароли PFX храните с ограниченными правами. В Git добавляйте код и конфигурационные шаблоны с заполнителями отпечатков. Для выпуска через внутренний УЦ используйте процедуру вашей организации.

### 4. Изменить Relay для WSS

Изменения выполняются в Peer/BIS.ERP.Relay/Program.cs поверх полного кода локального прототипа. Удалите строку builder.WebHost.UseUrls("http://127.0.0.1:5080"). Добавьте пространства имён и настройку Kestrel перед builder.Build().

```csharp
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

// После WebApplication.CreateBuilder(args)
var relayThumbprint = builder.Configuration["Relay:CertificateThumbprint"]
    ?? throw new InvalidOperationException("Relay certificate is required.");
var allowedA = builder.Configuration["Relay:NodeAThumbprint"]
    ?? throw new InvalidOperationException("Node A certificate is required.");
var allowedB = builder.Configuration["Relay:NodeBThumbprint"]
    ?? throw new InvalidOperationException("Node B certificate is required.");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5443, endpoint =>
    {
        endpoint.UseHttps(https =>
        {
            https.ServerCertificate = LoadCertificate(relayThumbprint);
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (certificate, chain, errors) =>
                errors == SslPolicyErrors.None &&
                (Matches(certificate, allowedA) || Matches(certificate, allowedB));
        });
    });
});
```

В обработчике /peer замените получение id из строки запроса и исходную проверку на следующий блок. ID теперь определяется сертификатом. Остальной код маршрутизации сохраняется.

```csharp
var clientCertificate = await context.Connection.GetClientCertificateAsync();
var id = clientCertificate is not null && Matches(clientCertificate, allowedA)
    ? "A"
    : clientCertificate is not null && Matches(clientCertificate, allowedB)
        ? "B"
        : null;

if (!context.WebSockets.IsWebSocketRequest || id is null)
{
    context.Response.StatusCode = 403;
    return;
}
```

В конец файла Program.cs перед объявлением RelaySession добавьте локальные функции.

```csharp
static bool Matches(X509Certificate2 certificate, string thumbprint) =>
    string.Equals(
        certificate.Thumbprint,
        thumbprint.Replace(" ", ""),
        StringComparison.OrdinalIgnoreCase);

static X509Certificate2 LoadCertificate(string thumbprint)
{
    using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
    store.Open(OpenFlags.ReadOnly);
    var certificates = store.Certificates.Find(
        X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), validOnly: true);
    if (certificates.Count != 1 || !certificates[0].HasPrivateKey)
        throw new InvalidOperationException("Expected one valid certificate with a private key.");
    return certificates[0];
}
```

В appsettings.json Relay добавьте секцию, сохранив существующие настройки Logging. Отпечатки замените фактическими значениями.

```json
"Relay": {
  "CertificateThumbprint": "SERVER_CERTIFICATE_THUMBPRINT",
  "NodeAThumbprint": "A_CERTIFICATE_THUMBPRINT",
  "NodeBThumbprint": "B_CERTIFICATE_THUMBPRINT"
}
```

Проверьте, что в appsettings и переменных окружения отсутствуют другие Kestrel Endpoints, открывающие лишний HTTP-порт. Запускайте с --no-launch-profile. Выбранная настройка ListenAnyIP должна использоваться вместе с ограничением входящего доступа на брандмауэре.

### 5. Изменить PeerHost для WSS

В Peer/BIS.ERP.PeerHost/Worker.cs добавьте using System.Security.Cryptography.X509Certificates. Замените проверку relay.Scheme и IsLoopback следующим кодом.

```csharp
if (relay.Scheme != "wss")
    throw new InvalidOperationException("A wss Relay URL is required.");
var thumbprint = configuration["Peer:CertificateThumbprint"]
    ?? throw new InvalidOperationException("Client certificate is required.");
```

Сразу после создания ClientWebSocket внутри цикла добавьте загрузку сертификата и назначение его сокету.

```csharp
using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
store.Open(OpenFlags.ReadOnly);
var certificates = store.Certificates.Find(
    X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), validOnly: true);
if (certificates.Count != 1 || !certificates[0].HasPrivateKey)
    throw new InvalidOperationException("Expected one valid client certificate with a private key.");
using var clientCertificate = certificates[0];
socket.Options.ClientCertificates.Add(clientCertificate);
```

Стандартная проверка сертификата сервера должна оставаться включённой. RemoteCertificateValidationCallback, безусловно возвращающий true, в этом варианте не используется. Имя Relay должно разрешаться в нужный адрес и совпадать с сертификатом сервера.

В appsettings.json узла A добавьте секцию Peer. На B замените NodeId на B и отпечаток на сертификат B.

```json
"Peer": {
  "NodeId": "A",
  "RelayUrl": "wss://biis-relay.corp.example:5443/peer",
  "CertificateThumbprint": "A_CERTIFICATE_THUMBPRINT"
}
```

Старое значение по умолчанию ws://127.0.0.1:5080/peer в Worker замените на обязательную настройку конфигурации, чтобы отсутствие параметра было явной ошибкой.

```csharp
var relay = new Uri(configuration["Peer:RelayUrl"]
    ?? throw new InvalidOperationException("Peer:RelayUrl is required."));
```

После этих изменений локальная проверка также выполняется через WSS. Пересоберите оба проекта командами из практического раздела. При dotnet run настройки читаются из каталога проекта. При публикации разместите нужный appsettings.json рядом с exe и проверьте content root до регистрации службы.

### 6. Интернет через домашний сервер

Домашний сервер может быть публичной точкой BIIS, даже если его собственный адрес 192.168.x.x. Важно, каким способом до него дойдёт входящее соединение.

#### Проверка домашнего подключения

1. Откройте статус WAN домашнего роутера и посмотрите IPv4, выданный провайдером.
2. Сравните его с внешним IPv4, который виден из домашней сети через браузер.
3. WAN из диапазонов 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 или 100.64.0.0/10 означает дополнительный NAT. Внешний IPv4 сам по себе не подтверждает, что адрес назначен вашему роутеру.
4. Если перед вашим роутером стоит ещё один ваш маршрутизатор, настройте проброс на обоих или переведите внешнее устройство в подходящий режим. При CGNAT провайдера потребуется его участие.
5. Уточните у провайдера услугу публичного IPv4 и возможность входящих соединений. Статический адрес упрощает эксплуатацию. Публичный динамический IPv4 подходит для теста, но требует обновления адреса при изменении.
6. Если публичного IPv4 нет, проверьте глобальный IPv6 по следующему разделу.

#### Домашний сервер с публичным IPv4 на роутере

1. Закрепите локальный адрес сервера, например 192.168.1.50.
2. Настройте WSS и сертификаты по разделам 3–5. Для интернета используйте реальное имя, например biis-relay.your-domain.example, заменив пример собственным DNS-именем. Укажите это имя в SAN серверного сертификата и RelayUrl.
3. Добавьте DNS A-запись с публичным IPv4 роутера. Если DNS-сервиса в рамках вашего ограничения нет, для небольшой фиксированной группы можно прописать имя и публичный IP в hosts на A и B. При смене IP обновите записи.
4. В роутере создайте Port Forwarding/Virtual Server. Протокол TCP, внешний порт 5443, внутренний адрес 192.168.1.50, внутренний порт 5443. Адрес сервера должен оставаться постоянным.
5. Разрешите TCP 5443 в брандмауэре сервера. Если внешние адреса A и B фиксированы, укажите их в RemoteAddress. Иначе правило ниже открывает порт для любого адреса, а доступ к протоколу контролируется mTLS и списком сертификатов.

```powershell
New-NetFirewallRule -DisplayName "BIIS Relay Internet 5443" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5443
```

6. Запустите Relay, проверьте слушатель на сервере и Test-NetConnection с внешнего подключения, например с компьютера через мобильный интернет. Проверка из той же домашней сети может зависеть от поддержки NAT loopback.
7. На A и B укажите публичное имя в Peer:RelayUrl и соответствующие сертификаты. Разрешите исходящий TCP 5443 в сети каждой площадки. Если сеть допускает только 443, согласуйте этот порт и замените его во всех настройках.
8. Запустите узлы, проверьте pong, перезапуск Relay и восстановление после краткого отключения интернета.
9. После успешной проверки опубликуйте компоненты и зарегистрируйте службы по пункту 7 основного плана. A и B устанавливают только исходящие подключения к Relay, проброс портов на их роутерах для этого обмена не требуется.

#### Вариант с глобальным IPv6

Адрес fe80:: относится к локальному каналу. Для подключения через интернет нужен глобальный маршрутизируемый адрес. Попросите провайдера подтвердить наличие IPv6 и делегированного префикса.

1. Проверьте, что Relay получает глобальный IPv6, а A и B имеют работающий исходящий IPv6 до него.
2. Настройте AAAA-запись выбранного имени на IPv6 сервера. При меняющемся префиксе предусмотрите обновление записи.
3. Разрешите входящий TCP 5443 в IPv6 firewall роутера и Windows. При обычной маршрутизируемой схеме IPv6 используется разрешающее правило, а адрес назначения принадлежит самому серверу.
4. Выполните Test-NetConnection по имени с обеих площадок, затем подключите WSS.
5. Если одна площадка имеет только IPv4, прямой IPv6-вариант ей недоступен. Для общего подключения потребуется публичная точка с доступной ей адресацией.

### 7. Диагностика по этапам

| Симптом | Что проверить |
|---|---|
| Имя не найдено | DNS A/AAAA или hosts, фактическое имя в RelayUrl |
| TCP недоступен между подсетями | Маршруты туда и обратно, VLAN ACL, Windows firewall |
| TCP недоступен из интернета | Публичный WAN, CGNAT, проброс, фильтры провайдера, внешний тест |
| TCP доступен, WSS не подключается | Сертификат сервера, SAN, цепочку доверия, срок, часы |
| Relay отвергает клиента | Наличие ключа у A/B, права на ключ, доверие клиентскому УЦ, отпечаток |
| После запуска службы сертификат не найден | LocalMachine/My, конфигурацию и учётную запись службы |
| Node already connected | Лишний экземпляр PeerHost с тем же сертификатом/ID |
| unavailable | Второй узел отсутствует на Relay |
| pong прерывается | Журналы трёх процессов, доступность Relay, тайм-ауты и перезапуск |

При открытом TCP браузер без клиентского сертификата может не открыть WSS-сервис. Рабочий критерий — аутентифицированные узлы и совпадающие ID ping/pong.

### 8. Приёмка сетевого этапа

Сохраните логи A, B и Relay. Убедитесь в обмене A → B и B → A, восстановлении после перезапуска Relay и недоступности сервиса с чужим сертификатом. Затем проверьте запуск служб после перезагрузки Windows. На этом этапе подтверждён транспорт. Подключение бизнес-операций, журнал команд и защита записи от повторов выполняются следующими пунктами основного плана.

Справочные материалы Microsoft:

- [Настройка HTTPS и конечных точек Kestrel](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0)
- [Проверка клиентских сертификатов](https://learn.microsoft.com/aspnet/core/security/authentication/certauth?view=aspnetcore-8.0)
- [Правила брандмауэра New-NetFirewallRule](https://learn.microsoft.com/powershell/module/netsecurity/new-netfirewallrule)
- [Проверка TCP через Test-NetConnection](https://learn.microsoft.com/powershell/module/nettcpip/test-netconnection)
